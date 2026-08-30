# 数据库数据核验脚本（验证完成后可删除）
import sqlite3

conn = sqlite3.connect(r'd:\work\chenzhongruanjian\src\CZRWeighSystem\bin\Debug\net10.0-windows\data\weigh.db')
cur = conn.cursor()

print('== t_user ==')
for r in cur.execute('SELECT username, display_name, role, enabled, is_default_pwd FROM t_user'):
    print(r)

n = cur.execute('SELECT COUNT(*) FROM t_weigh_record').fetchone()[0]
print('== t_weigh_record count ==', n)

print('== latest records ==')
for r in cur.execute('SELECT serial_no, vehicle_no, gross_kg, tare_kg, net_kg, status, is_manual FROM t_weigh_record ORDER BY id DESC LIMIT 10'):
    print(r)

print('== daily summary (已完成) ==')
for r in cur.execute(
    "SELECT substr(first_time,1,10), COUNT(*), SUM(gross_kg), SUM(tare_kg), SUM(net_kg) "
    "FROM t_weigh_record WHERE status='已完成' GROUP BY 1"):
    print(r)

print('== summary by vehicle (已完成) ==')
for r in cur.execute(
    "SELECT vehicle_no, COUNT(*), SUM(net_kg) FROM t_weigh_record "
    "WHERE status='已完成' GROUP BY vehicle_no"):
    print(r)

conn.close()
