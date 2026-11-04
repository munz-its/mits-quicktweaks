import json
HKCU, HKLM = "HKCU", "HKLM"
ADV = r"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
CDM = r"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"
MM = r"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"
W11 = 22000

def reg(h, k, n, v, d=None, kind="DWord"):
    a = {"type": "registry", "hive": h, "key": k, "name": n, "kind": kind, "value": str(v)}
    if d is not None: a["default"] = str(d)
    return a
def sz(h, k, n, v, d=None): return reg(h, k, n, v, d, "String")
def svc(name, start, default): return {"type": "service", "name": name, "startType": start, "default": default}
def t(id, cat, risk, restart, de_t, en_t, de_d, en_d, actions, minb=19045, maxb=0):
    o = {"id": id, "category": cat, "risk": risk, "restart": restart, "minBuild": minb}
    if maxb: o["maxBuild"] = maxb
    o["title"] = {"de": de_t, "en": en_t}
    o["description"] = {"de": de_d, "en": en_d}
    o["actions"] = actions
    return o
