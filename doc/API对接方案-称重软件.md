# 称重软件对接 API 设计

> 场景：公平计量站现场部署一套**称重软件**（PC 端，与我方同一团队开发，字段完全可控），
> 定期向平台推送称重记录；平台根据记录中的**电话（地主电话 / 司机电话）**自动匹配归属
> （哪个端、哪个用户），使记录在各端 App 与管理后台可见、可管。
> 同时承接两类附属数据：**过磅抓拍照片**（随记录上传）与**黑匣子记录**（重量 + 时间 + 抓拍）。

| 项 | 说明 |
| --- | --- |
| 对接方 | 称重软件（服务端对服务端，非用户登录态） |
| 后端现状 | Django 5.1，已有 `WeighingRecord`（biz_weighing_record）模型与管理端 CRUD/作废 |
| 协议 | HTTPS + JSON（照片上传为 multipart/form-data），POST 为主 |
| 鉴权 | AppKey/AppSecret + HMAC-SHA256 签名（防篡改 + 防重放） |
| 幂等 | 称重软件侧唯一流水号 `clientRef`，服务端唯一索引去重 |

## 1. 现状与总体思路

### 1.1 平台已有能力（本方案直接复用）

- **称重记录模型** `WeighingRecord`：计量站（必填外键）、司磅员、农户手机号/姓名、车牌、品种、毛重/皮重/净重（服务端自动算净重）、称重单号（服务端生成 `CZ+站编码后3位+日期+4位流水`）、状态（valid/void）与作废留痕字段。
- **分端数据范围** `_scope_weighing`：农户端按"手机号 = farmer_phone"看自己的称重记录；验粮端/站端按绑定计量站看同站记录。
- **端与角色映射**：一个底座 `User` 可持有多个业务角色（R_FARMER/R_INSPECTOR/R_BUYER/R_ENTERPRISE/R_DRIVER/R_SERVICE/R_STATION），每个角色对应一个端。
- **存储服务** `services/storage.py` 与统一上传入口（照片落 OSS/本地均可复用）。

### 1.2 缺口（本方案补齐）

1. **系统级接入凭证**：称重软件不是登录用户，需要"接入方 ↔ 计量站"的绑定凭证。
2. **开放推送接口**：`/api/open/...` 匿名可访问但强签名校验，落库走独立入口（不进管理端 CRUD）。
3. **双电话归属匹配**：软件表单同时有**地主电话**与**司机电话**，需分别匹配用户并留痕，端由匹配用户的角色派生。
4. **抓拍照片上传**：过磅抓拍照先传后绑，随记录关联展示。
5. **黑匣子记录**：独立轻量数据（重量 + 时间 + 抓拍），独立模型与批量接口。
6. **幂等与追溯**：外部唯一号 `clientRef`、数据来源 `source`、称重业务时间 `weighTime`。

### 1.3 软件表单字段映射（全保留）

过磅单表单（空车去皮 / 重车过毛 两段式）与接口字段的对应关系：

| 软件表单字段 | 接口字段 | 平台字段 | 说明 |
| --- | --- | --- | --- |
| No（磅单号） | `clientRef` | `client_ref` | 幂等键 |
| 日期 / 称重时间 | `weighTime` | `weigh_time` | 离线补传时以软件时间为准 |
| 车牌号码 | `plateNo` | `plate_no` | 含省份缩写，如 `冀E` |
| 毛重 | `grossWeight` | `gross_weight` | kg |
| 皮重 | `tareWeight` | `tare_weight` | kg |
| 净重 | —（不传） | `net_weight` | 服务端计算 毛−皮 |
| 地主姓名 | `landownerName` | `farmer_name` | 地主≈农户，沿用现有字段 |
| **地主电话** | `landownerPhone` | `farmer_phone` | **归属匹配依据①**（农户端口径零改造） |
| **司机电话** | `driverPhone` | `driver_phone`（新增） | **归属匹配依据②**（司机端） |
| 货主姓名 | `cargoOwnerName` | `cargo_owner_name`（新增） | 表单无货主电话，仅快照留存 |
| 品种 | `variety` | `variety` | |
| 司磅员 | `weigher` | `weigher` | |
| 抓拍照片 | `photos[]` | 附件表（新增） | 先传照片拿 photoId，再随记录引用 |

> 界面上的"上次重量 / 上次时间 / 预计净重"为软件本地运算状态，不需上传；
> "公司名称"为软件配置抬头，平台侧以凭证绑定的计量站为准。

**推送单位约定**：一条记录 = 一次**完整称重业务**（毛重、皮重两次过磅均完成后由软件组装推送）。
如后续需要按"过磅趟次"分别成单，另行扩展 `phase` 字段，本期不做。

## 2. 接入模型与凭证

```
称重软件安装实例（1 套/站点）
    └── 1 张接入凭证（AppKey + AppSecret，绑定 1 个计量站 Station）
            └── 推送的每条记录自动归属该计量站，无需再传 stationId
```

新增模型 `WeighingApiCredential`（表 `biz_weighing_api_credential`）：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| station | FK(Station) | 绑定的计量站（称重记录的 station 由此确定） |
| app_key | Char(32) unique | 接入标识，明文存储 |
| app_secret | Char(128) | 接入密钥，**加密存储**；仅生成/重置时明文展示一次 |
| enabled | Boolean | 启用开关（默认 True），停用后所有请求 401 |
| last_seen_at | DateTime null | 最近一次成功鉴权时间（排查失活） |
| remark | Char(255) | 备注（如"1 号地磅""东门磅房"） |

管理端能力（挂到现有"计量站管理"）：**生成 / 重置 / 启用 / 停用**；密钥掩码显示，重置后旧密钥立即失效。同一计量站允许多张凭证（多台地磅各一套软件）。

## 3. 鉴权与安全

### 3.1 请求头

| Header | 说明 |
| --- | --- |
| `X-Api-Key` | 接入标识 |
| `X-Timestamp` | 毫秒时间戳，与服务器偏差 **±300 秒** 内有效 |
| `X-Nonce` | 随机串（≥16 位），5 分钟内不重复（Redis 缓存去重，防重放） |
| `X-Signature` | 签名，见 3.2 |

### 3.2 签名算法（HMAC-SHA256）

照片上传为 multipart 请求，签名时以"元数据串"代替请求体哈希，其余接口一致：

```
JSON 接口:  bodyHash = hex( SHA-256( 原始请求体 UTF-8 ) )
照片上传:   bodyHash = hex( SHA-256( appKey + timestamp + fileSize + fileSha256 ) )   # fileSha256 由软件对文件计算后放入表单字段
signStr  = appKey + "\n" + timestamp + "\n" + nonce + "\n" + bodyHash
signature = hex( HMAC-SHA256( appSecret, signStr ) )
```

- 服务端按同样顺序计算并 `constant-time` 比对；任一环节失败返回 `401 + code=1000`，不区分具体原因（避免给攻击者探测信息），失败详情写运营日志。
- 强制 HTTPS；后续可按 appKey 追加 IP 白名单（本期不强制，预留校验位）。

### 3.3 限流

按 appKey 限流：**数据接口 60 次/分钟、照片上传 120 次/分钟**（Redis 计数），超限返回 `429`。黑匣子抓拍频率较高，上传阈值放宽。

## 4. 接口定义

统一响应包裹沿用平台契约：`{ code, msg, data }`；`code=0000` 成功、`1000` 鉴权失败、`5000` 业务失败。**HTTP 状态码只表达传输层结果（401/429/500），业务校验结果在 `data` 中逐条返回，批量不因部分失败而整体报错。**

接口总览：

| # | 接口 | 方法 | 用途 |
| --- | --- | --- | --- |
| 1 | `/api/open/weighing/ping` | GET | 连通性自检（测试连接按钮） |
| 2 | `/api/open/weighing/photos` | POST(multipart) | 上传抓拍照片，返回 photoId |
| 3 | `/api/open/weighing/records` | POST(JSON) | 推送称重记录（引用 photoId） |
| 4 | `/api/open/weighing/blackbox` | POST(JSON) | 批量推送黑匣子记录 |
| 5 | `/api/open/weighing/void` | POST(JSON) | 作废称重记录 |

### 4.1 连通性自检（ping）

```
GET /api/open/weighing/ping
```

返回 `data`: `{ "stationCode": "CZ001", "stationName": "XX公平计量站" }`。
供称重软件"测试连接"按钮使用，验证密钥、时钟偏差与网络连通。

### 4.2 上传抓拍照片

```
POST /api/open/weighing/photos
Content-Type: multipart/form-data
表单字段: file(图片二进制), fileSha256(文件哈希, 参与签名), shotAt(抓拍时间, 可选), clientRef(照片流水号, 软件侧唯一, 幂等)
```

约束：jpg/png，单张 ≤10MB；同一 `clientRef` 重复上传返回已有 photoId（幂等）。

响应（200）：

```json
{ "code": "0000", "msg": "", "data": { "photoId": "ph_9f8e7d6c", "url": "https://.../ph_9f8e7d6c.jpg" } }
```

**生命周期**：上传后照片处于"未挂载"状态，**24 小时**内未被任何记录/黑匣子记录引用则由清理任务删除（平台已有调度器基础设施）；被引用后随所属数据永久保留。上传与推送解耦，软件可先批量传照片再推记录，推送失败重试时照片不重复上传。

### 4.3 推送称重记录

```
POST /api/open/weighing/records
Content-Type: application/json
```

请求体：

```json
{
  "records": [
    {
      "clientRef": "CZ001-20260930-0001",
      "weighTime": "2026-09-30 10:35:00",
      "plateNo": "冀E12345",
      "grossWeight": 32550.5,
      "tareWeight": 12550.5,
      "landownerName": "张三",
      "landownerPhone": "13800138000",
      "driverPhone": "13900139000",
      "cargoOwnerName": "李四",
      "variety": "玉米",
      "weigher": "管理员",
      "photos": ["ph_9f8e7d6c", "ph_8a7b6c5d"]
    }
  ]
}
```

字段说明（`records` 数组，**1~50 条/次**，超出拆批）：

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| clientRef | string ≤64 | 是 | 称重软件侧唯一流水号（幂等键）。建议 `{站编码}-{yyyyMMdd}-{流水}`；同号重复推送视为重复，不重复入库 |
| weighTime | datetime | 是 | 称重完成时间（本地东八区，支持离线补传历史记录） |
| plateNo | string ≤16 | 否 | 车牌号 |
| grossWeight | number ≥0 | 是 | 毛重(kg)，两位小数 |
| tareWeight | number ≥0 | 是 | 皮重(kg)，两位小数；净重服务端计算（毛−皮），**无需传净重** |
| landownerName | string ≤32 | 否 | 地主姓名快照 |
| landownerPhone | string | 是 | 11 位手机号，**归属匹配依据①** |
| driverPhone | string | 否 | 11 位手机号，**归属匹配依据②**；无司机信息可不传 |
| cargoOwnerName | string ≤32 | 否 | 货主姓名快照（表单无对应电话项） |
| variety | string ≤32 | 否 | 品种 |
| weigher | string ≤32 | 否 | 司磅员 |
| photos | string[] | 否 | photoId 列表，**≤9 张**，须为本凭证已上传且未过期的照片；引用后照片挂载到该记录 |

响应（200）：

```json
{
  "code": "0000",
  "msg": "",
  "data": {
    "accepted": 1,
    "results": [
      {
        "clientRef": "CZ001-20260930-0001",
        "status": "created",
        "recordNo": "CZ001202609300001",
        "landownerMatched": true,
        "driverMatched": false,
        "reason": ""
      }
    ]
  }
}
```

| results.status | 含义 | 匹配标记语义 |
| --- | --- | --- |
| created | 新建成功 | `landownerMatched`/`driverMatched` 分别表示两条电话是否匹配到用户；false 时记录仍入库，待人工补绑 |
| duplicate | clientRef 已存在（幂等命中） | 返回既有记录的匹配结果，**不更新不报错** |
| failed | 校验失败未入库 | 均为 false；`reason` 说明原因（必填缺失/电话非法/负数/photoId 不存在等） |

### 4.4 推送黑匣子记录

黑匣子记录只含 **重量、时间、抓拍**，是称重过程的自动留痕（防作弊证据），不参与归属匹配、不进各端 App，仅管理端（及站端，可选）可查。

```
POST /api/open/weighing/blackbox
{ "records": [
    { "clientRef": "CZ001-BX-20260930-000001", "weight": 32550.5,
      "weighedAt": "2026-09-30 10:34:12", "photoId": "ph_7a6b5c4d" }
] }
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| clientRef | string ≤64 | 是 | 黑匣子记录流水号（幂等键，独立于称重记录） |
| weight | number ≥0 | 是 | 瞬时重量(kg) |
| weighedAt | datetime | 是 | 抓拍时间 |
| photoId | string | 是 | 抓拍照片（黑匣子记录必带照片） |

- 批量 **1~200 条/次**（抓拍频率高，阈值放宽）；逐条返回 `created / duplicate / failed`。
- 新模型 `WeighingBlackboxRecord`（表 `biz_weighing_blackbox_record`）：station、client_ref(unique)、weight、weighed_at、photo(FK 附件表)、created_at。无作废/修改语义，只增不改。

### 4.5 作废称重记录

称重软件现场作废（如错车、倒车重称）后同步平台：

```
POST /api/open/weighing/void
{ "clientRef": "CZ001-20260930-0001", "reason": "车牌识别错误重称" }
```

- 复用现有作废语义：`status=void`，`void_reason` 记录原因，`voided_by` 记 NULL（来源为外部软件），留痕不可物理删除。
- 响应 `data`: `{ "clientRef", "status": "voided | not_found | already_voided", "reason": "" }`；作废**不可逆**，恢复需管理端操作。

## 5. 归属匹配规则（电话 → 哪个端、哪个用户）

核心原则：**"端"不由推送方指定，而是由匹配到的用户所持业务角色派生**（一个手机号对应一个底座 User，一个 User 可有多端角色）。一条记录有两条匹配依据，**各自独立匹配、互不影响**：

- **地主电话**（`landownerPhone`）→ 匹配结果写入 `landowner_user`；地主即售粮方，优先匹配农户端角色。
- **司机电话**（`driverPhone`）→ 匹配结果写入 `driver_user`；优先匹配司机端角色。
- 地主电话同时落到 `farmer_phone` 字段，农户端现有"手机号看称重记录"口径**零改造**即生效。

### 5.1 单条电话的匹配算法

```
1. 归一化 phone（去空格，校验 ^1\d{10}$；地主电话非法直接 failed，司机电话非法仅告警并置空）
2. 候选集 = User.filter(phone=phone, is_active=True, 未注销)，且至少持有一个业务角色
3. 命中 0 个  → 该电话未匹配（对应 user 字段 = NULL）
4. 命中 1 个  → 写入对应 user 字段
5. 命中 ≥2 个 → 消歧优先级：
   a. 与电话语义相符的角色优先（地主电话→farmer；司机电话→driver）
   b. 其余按固定顺序：farmer > buyer > driver > inspector > enterprise > service > station
   c. 仍并列 → 取注册最早（id 最小）者，保证结果确定性
```

记录级 `match_status` 由两条电话的匹配结果汇总：

| match_status | 含义 |
| --- | --- |
| matched | 两条电话全部匹配（或司机电话未传且地主已匹配） |
| partial | 一条匹配、另一条传了但未匹配 |
| unmatched | 传了的电话全部未匹配（记录照常入库） |

### 5.2 模型变更（`biz_weighing_record` 增量字段）

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| client_ref | Char(64) unique null | 外部流水号；NULL 表示管理端/App 手工创建 |
| source | Char(16)，默认 `management` | `management`（后台录入）/ `software`（称重软件推送） |
| weigh_time | DateTime null | 称重业务时间；空则前端回落 created_at |
| driver_phone | Char(11)，默认 "" | 司机电话（表单新字段） |
| cargo_owner_name | Char(32)，默认 "" | 货主姓名快照 |
| landowner_user | FK(User) null | 地主电话匹配用户 |
| driver_user | FK(User) null | 司机电话匹配用户 |
| match_status | Char(16)，默认 `matched` | matched / partial / unmatched |

存量数据：source=management、client_ref=NULL、新增字段空默认，迁移零影响；现有 farmer_phone/farmer_name 语义不变（地主即农户）。

附件表 `biz_weighing_photo`（照片统一挂载点，称重记录与黑匣子共用）：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| station | FK(Station) | 上传凭证绑定的站 |
| client_ref | Char(64) unique | 照片流水号（上传幂等） |
| file | 文件路径/URL | 复用 storage 服务落盘 |
| file_size / sha256 | - | 校验与去重辅助 |
| shot_at | DateTime null | 抓拍时间 |
| target_type | Char(16) | `record` / `blackbox` |
| target_id | int null | 挂载目标 ID；NULL=未挂载（24h 过期清理） |

### 5.3 未匹配记录的处理

- **照常入库**（记录事实优先，不因"人没注册"丢数据），仅标记对应电话未匹配。
- 管理端称重列表增加 `matchStatus` 筛选；详情页提供 **"重新匹配"** 动作：对两条电话分别重跑 §5.1 算法刷新匹配结果——覆盖"先称重、后注册"的时序，无需定时任务。
- 站端视角（同站列表）不受匹配影响，照常可见。

### 5.4 各端可见性（现状沿用 + 可选扩展）

| 端 | 可见范围 | 是否需改造 |
| --- | --- | --- |
| 农户端（地主） | `farmer_phone = 本人手机号` | 否（现有口径，地主电话落此字段） |
| 验粮端 / 站端 | 同计量站的记录 | 否（现有口径） |
| 司机端 | 现状不可见 | **本期扩展**：`_scope_weighing` 增加 driver 分支 `Q(driver_phone=user.phone)`，司机端即可看到自己运输的称重单 |
| 管理后台 | 全部 + matchStatus 筛选 + 重新匹配 + 黑匣子台账 | 是（本方案新增） |

## 6. 错误码与重试约定

| 场景 | HTTP | code | 称重软件侧处理 |
| --- | --- | --- | --- |
| 鉴权失败（密钥/签名/时间戳/nonce/停用） | 401 | 1000 | 停止重试，提示检查密钥与系统时间 |
| 参数结构错误（非 JSON、records 为空、超条数上限） | 200 | 5000 | 修正后重发整批 |
| 单条业务校验失败 | 200 | 0000 | results 中 `failed` + reason，**本地修正后仅重发失败条目** |
| 照片过大 / 格式不符 | 200 | 5000 | 压缩或跳过，记录可无照片推送 |
| 限流 | 429 | 5000 | 退避 ≥10s 重试 |
| 服务器异常 / 网络 | 5xx / 超时 | - | **指数退避重试**（1m/5m/15m/1h/6h，最多 5 次）；本地队列缓存未确认数据，断网续传 |

幂等性保证重试安全：同一 clientRef 重复推送（记录/黑匣子/照片均同）恒返回 `duplicate`，不会产生脏数据。

**黑匣子上报节奏约定**：软件本地缓存抓拍队列，定时（建议 ≥1 分钟间隔）批量上报；断网期间本地留存，恢复后续传，不做实时逐条上报。

## 7. 联调与验收清单

1. ping 自检：正确密钥返回站名；篡改任一签名参数 → 401。
2. 时间戳偏差 >300s → 401；相同 nonce 重放 → 401。
3. 照片上传 → 返回 photoId 且可访问；同 clientRef 重传 → 幂等返回同 ID；未引用照片 24h 后被清理。
4. 单条推送（含两张照片、地主+司机双电话）→ `created`，管理端可见，照片挂在记录详情，净重 = 毛−皮，称重单号按站编码生成。
5. 地主电话已注册农户 → 农户端"称重记录"可见该单；司机电话已注册 → 司机端可见。
6. 双电话均未注册 → `unmatched`，管理端可"重新匹配"，注册同号后补绑成功。
7. 同 clientRef 重推 → `duplicate`，库中仍一条。
8. 批量 50 条含 3 条非法 → 47 条 `created`，3 条 `failed` 带 reason。
9. 黑匣子批量 200 条（含重复）→ 新建/幂等正确，照片挂载正确，管理端黑匣子台账可按站/时间筛选。
10. 称重软件作废 → 两端状态同步为"已作废"且原因可见。
11. 断网续传按 §6 策略演练一次（含照片先传后绑）。

## 8. 实施范围（后续开发拆解）

| # | 任务 | 涉及 |
| --- | --- | --- |
| 1 | `WeighingApiCredential` 模型 + 迁移 + 管理端凭证管理（生成/重置/启停） | models、views/system |
| 2 | `WeighingRecord` 增量字段迁移；`WeighingPhoto` / `WeighingBlackboxRecord` 新表 | models/business |
| 3 | 签名鉴权服务（HMAC 校验、multipart 签名、nonce 去重、分接口限流）+ `/api/open/` 路由 | services、views/open/weighing.py、urls.py |
| 4 | 照片上传视图 + 24h 未挂载清理任务 | views/open、scheduler |
| 5 | 称重记录推送/作废/ping 视图 + 双电话归属匹配服务 | views/open、services/weighing_match.py |
| 6 | 黑匣子批量推送视图 + 管理端黑匣子台账（按站/时间筛选） | views/open、views/business |
| 7 | `_scope_weighing` 司机端分支（driver_phone 匹配） | views/app/scope.py |
| 8 | 管理端：matchStatus 筛选 + 重新匹配 action | views/business/weighing.py |
| 9 | 测试：签名与重放、multipart 签名、照片幂等与过期、双电话匹配全分支、黑匣子批量幂等、作废 | tests_weighing_open.py |
