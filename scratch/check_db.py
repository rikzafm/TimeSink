import sqlite3
import os

db_path = os.path.expandvars(r'%LocalAppData%\TimeSink\timesink.db')
print('DB exists:', os.path.exists(db_path))
if os.path.exists(db_path):
    conn = sqlite3.connect(db_path)
    cur = conn.cursor()
    cur.execute("SELECT id, display_name, exe_path, kind, canonical_id, is_background FROM apps WHERE display_name LIKE '%Antigravity%';")
    for row in cur.fetchall():
        print(row)

