import json, datetime, os
ROOT = "/home/claude/mockups/project"

BG="#0a0c10"; BG2="#11141a"; BG3="#171b23"; TEXT="#f2f4f8"; TEXT2="#d3d8e0"; MUTED="#b6bdc9"; LINE="#262b35"
BLUE="#1f5eff"; BLUEL="#5b8cff"; BLUEXL="#8fa9ff"; OK="#3dd68c"; WARN="#ffb547"
DISP="'Archivo', sans-serif"; SANS="'IBM Plex Sans', sans-serif"; MONO="'IBM Plex Mono', monospace"

def pixel_svg(pattern, size=14, color="currentColor"):
    rows = pattern.split("/"); n = 5; cell = 10; gap = 2
    rects = "".join(f'<rect x="{x*cell}" y="{y*cell}" width="{cell-gap}" height="{cell-gap}"></rect>'
                    for y,r in enumerate(rows) for x,c in enumerate(r) if c=="1")
    return f'<svg width="{size}" height="{size}" viewBox="0 0 50 50" fill="{color}" aria-hidden="true">{rects}</svg>'

def logo(size=22):
    blocks=[(5,77),(77,77),(5,59),(77,59),(5,41),(77,41),(5,23),(23,23),(59,23),(77,23),(5,5),(77,5)]
    g=""
    for x,y in blocks:
        g+=(f'<polygon fill="#FFFFFF" points="{x},{y} {x+5},{y-5} {x+19},{y-5} {x+14},{y}"></polygon>'
            f'<polygon fill="#6F7889" points="{x+14},{y} {x+19},{y-5} {x+19},{y+9} {x+14},{y+14}"></polygon>'
            f'<rect fill="#D9DEE6" x="{x}" y="{y}" width="14" height="14"></rect>')
    g+=('<polygon fill="#84A6FF" points="41,41 46,36 60,36 55,41"></polygon>'
        '<polygon fill="#143DA6" points="55,41 60,36 60,50 55,55"></polygon>'
        '<rect fill="#1F5EFF" x="41" y="41" width="14" height="14"></rect>')
    return f'<svg width="{size}" height="{size}" viewBox="0 -8 104 104" role="img" aria-label="Munz IT Service Logo">{g}</svg>'

NAV=[("Dashboard","11011/11011/00000/11011/11011"),("Tweaks","01000/11111/00000/00010/11111"),
     ("Datenschutz","01110/10001/11111/11011/11111"),("Debloat","11111/01110/01010/01010/01110"),
     ("Autostart","00100/10101/10101/10001/01110"),("System Care","00100/00100/11111/00100/00100"),
     ("Wiederherstellung","01110/10101/10111/10001/01110")]

def nav_item(label, pattern, active):
    bg = "background: rgba(31,94,255,0.14);" if active else ""
    col = TEXT if active else TEXT2
    marker = f'<span style="position: absolute; left: 4px; top: 50%; width: 4px; height: 4px; margin-top: -2px; background: {BLUE}"></span>' if active else ""
    return (f'<a href="#" style="position: relative; display: flex; align-items: center; gap: 12px; padding: 10px 14px; border-radius: 8px; '
            f'color: {col}; text-decoration: none; font-size: 14px; {bg}">{marker}<span style="color: {col}; display: flex">{pixel_svg(pattern)}</span>{label}</a>')

def sidebar(active):
    items="".join(nav_item(l,p,l==active) for l,p in NAV)
    return f'''<nav aria-label="Hauptnavigation" style="flex: 0 0 232px; box-sizing: border-box; background: {BG2}; border-right: 1px solid {LINE}; display: flex; flex-direction: column; padding: 0 12px 16px">
<div style="height: 56px; display: flex; align-items: center; gap: 10px; padding: 0 6px">{logo(22)}<span style="font-family: {DISP}; font-stretch: 118%; font-weight: 760; font-size: 14px; color: {TEXT}">Munz Quick Tweaks</span></div>
<div style="display: flex; flex-direction: column; gap: 2px; margin-top: 8px">{items}</div>
<div style="margin-top: auto; display: flex; flex-direction: column; gap: 8px">
<div style="height: 1px; background: {LINE}; margin: 0 4px"></div>
{nav_item("Einstellungen","10101/01110/11011/01110/10101", active=="Einstellungen")}
<span style="font-family: {MONO}; font-size: 12px; color: {MUTED}; padding-left: 14px">von Munz IT Service</span>
</div>
</nav>'''

def toggle(on, label):
    if on:
        return (f'<button type="button" aria-pressed="true" aria-label="{label}" style="flex: 0 0 auto; width: 48px; height: 28px; border-radius: 7px; border: 1px solid {BLUE}; background: {BLUE}; '
                f'display: flex; align-items: center; justify-content: flex-end; padding: 0 5px; cursor: pointer"><span style="width: 14px; height: 14px; background: #ffffff"></span></button>')
    return (f'<button type="button" aria-pressed="false" aria-label="{label}" style="flex: 0 0 auto; width: 48px; height: 28px; border-radius: 7px; border: 1px solid {LINE}; background: {BG3}; '
            f'display: flex; align-items: center; justify-content: flex-start; padding: 0 5px; cursor: pointer"><span style="width: 14px; height: 14px; background: {MUTED}"></span></button>')

def badge(text, medium=False):
    dot = WARN if medium else OK
    bg = "rgba(255,181,71,0.12)" if medium else "rgba(61,214,140,0.10)"
    return (f'<span style="display: inline-flex; align-items: center; gap: 6px; padding: 2px 8px; border-radius: 4px; background: {bg}; '
            f'font-family: {MONO}; font-size: 11px; color: {TEXT2}"><span style="width: 5px; height: 5px; background: {dot}"></span>{text}</span>')

def label(text, color=BLUEXL):
    return f'<span style="font-family: {MONO}; font-size: 12px; letter-spacing: 0.04em; color: {color}">{text}</span>'

CARD=f"background: {BG2}; border: 1px solid {LINE}; border-radius: 12px"

def page(title, active, content, h):
    return f'''<!doctype html>
<html lang="de">
<head>
<meta charset="utf-8">
<title>{title}</title>
<script src="./support.js"></script>
</head>
<body>
<x-dc>
<helmet>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link href="https://fonts.googleapis.com/css2?family=Archivo:wdth,wght@100..125,500..800&amp;family=IBM+Plex+Sans:wght@400;500;600&amp;family=IBM+Plex+Mono&amp;display=swap" rel="stylesheet">
<style>
body{{margin:0;background:{BG}}}
a{{color:{TEXT}}}a:hover{{color:#ffffff}}
</style>
</helmet>
<div style="min-height: {h}px; background: {BG}; color: {TEXT}; font-family: {SANS}; display: flex; flex-wrap: wrap; border: 1px solid {LINE}; box-sizing: border-box">
{sidebar(active)}
<main style="flex: 999 1 560px; min-width: 0; box-sizing: border-box; padding: 24px clamp(20px, 4vw, 48px) 48px">
<div style="max-width: 1000px; display: flex; flex-direction: column; gap: 24px">
{content}
</div>
</main>
</div>
</x-dc>
<script type="text/x-dc" data-dc-script data-props='{{"$preview":{{"width":1280,"height":{h}}}}}'>
class Component extends DCLogic {{
renderVals() {{
return {{}};
}}
}}
</script>
</body>
</html>
'''

def h1(t): return f'<h1 style="margin: 0; font-family: {DISP}; font-stretch: 118%; font-weight: 760; font-size: 32px; letter-spacing: -0.02em; line-height: 1.1">{t}</h1>'
def h2(t): return f'<h2 style="margin: 0; font-family: {DISP}; font-stretch: 118%; font-weight: 760; font-size: 17px">{t}</h2>'
def p(t, color=TEXT2, size=14): return f'<p style="margin: 0; color: {color}; font-size: {size}px; line-height: 1.55">{t}</p>'
def btn(t, primary=True):
    if primary:
        return f'<button type="button" style="border: none; border-radius: 9px; background: {BLUE}; color: #ffffff; font-family: {SANS}; font-weight: 600; font-size: 14px; padding: 12px 18px; min-height: 44px; cursor: pointer">{t}</button>'
    return f'<button type="button" style="border: 1px solid {LINE}; border-radius: 9px; background: {BG3}; color: {TEXT}; font-family: {SANS}; font-weight: 600; font-size: 14px; padding: 12px 18px; min-height: 44px; cursor: pointer">{t}</button>'

def stat(lbl, value, sub, accent=BLUEL):
    return f'''<div style="{CARD}; padding: 18px 20px; display: flex; flex-direction: column; gap: 6px">
{label(lbl, MUTED)}
<span style="font-family: {DISP}; font-stretch: 118%; font-weight: 760; font-size: 30px; color: {accent}; line-height: 1.1">{value}</span>
<span style="font-size: 13px; color: {TEXT2}">{sub}</span>
</div>'''

# ---------- Dashboard ----------
def profile_card(title, sub, pattern, count):
    return f'''<button type="button" style="{CARD}; padding: 18px; text-align: left; display: flex; flex-direction: column; gap: 10px; cursor: pointer; color: {TEXT}; font-family: {SANS}; min-height: 44px">
<span style="width: 36px; height: 36px; border-radius: 8px; background: rgba(31,94,255,0.14); display: flex; align-items: center; justify-content: center; color: {BLUEL}">{pixel_svg(pattern, 16)}</span>
<span style="font-weight: 600; font-size: 15px">{title}</span>
<span style="font-size: 13px; color: {TEXT2}; line-height: 1.45">{sub}</span>
<span style="font-family: {MONO}; font-size: 11px; color: {MUTED}">{count}</span>
</button>'''

def sysrow(k, v, color=TEXT):
    return f'<div style="display: grid; grid-template-columns: 140px minmax(0, 1fr); gap: 16px; padding: 10px 0; border-top: 1px solid {LINE}">{label(k, MUTED)}<span style="font-size: 14px; color: {color}">{v}</span></div>'

dash = f'''<div style="display: flex; flex-wrap: wrap; justify-content: space-between; align-items: flex-end; gap: 16px">
<div style="display: flex; flex-direction: column; gap: 8px">{label("DASHBOARD")}{h1("Willkommen zurück")}{p("Dein Windows in einem Blick. Jede Änderung ist erklärt und lässt sich zurücksetzen.")}</div>
<span style="font-family: {MONO}; font-size: 12px; color: {MUTED}">Version 0.6.0</span>
</div>
<div style="display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 16px">
{stat("AKTIVE TWEAKS", "12", "von 56 verfügbaren")}
{stat("WIEDERHERSTELLUNGSPUNKT", "Heute", "04.11.2026, 16:42 Uhr", TEXT)}
{stat("SYSTEMSCHUTZ", "Aktiv", "Laufwerk C: geschützt", OK)}
</div>
<div style="display: flex; flex-direction: column; gap: 12px">
{label("PROFILE")}
<div style="display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 16px">
{profile_card("Empfohlen", "Weniger Werbung und Ablenkung", "00100/01110/11111/01110/01010", "13 Tweaks")}
{profile_card("Gaming", "Mehr Leistung für Spiele", "01110/11111/10101/11111/01010", "11 Tweaks · 4 mittel")}
{profile_card("Datenschutz", "Weniger Daten an Microsoft", "01110/10001/11111/11011/11111", "19 Tweaks · 2 mittel")}
{profile_card("Alles zurücksetzen", "Originalzustand herstellen", "01110/10101/10111/10001/01110", "12 aktiv")}
</div>
</div>
<div style="display: grid; grid-template-columns: minmax(0, 3fr) minmax(0, 2fr); gap: 16px">
<section style="{CARD}; padding: 20px 22px; display: flex; flex-direction: column; gap: 12px">
{h2("Dein System")}
<div>
{sysrow("WINDOWS", "Windows 11 Home 24H2 (Build 26100.2314)")}
{sysrow("PROZESSOR", "AMD Ryzen 7 270H")}
{sysrow("GRAFIKKARTE", "NVIDIA GeForce RTX 5060 Laptop GPU")}
{sysrow("ARBEITSSPEICHER", "16 GB")}
{sysrow("RECHTE", "Administrator", OK)}
</div>
</section>
<section style="{CARD}; padding: 20px 22px; display: flex; flex-direction: column; gap: 14px">
{h2("Zuletzt geändert")}
<div style="display: flex; flex-direction: column; gap: 12px">
<div style="display: flex; gap: 12px; align-items: baseline"><span style="font-family: {MONO}; font-size: 12px; color: {MUTED}; flex: 0 0 44px">16:44</span><span style="font-size: 14px">Angewendet: Dateiendungen anzeigen</span></div>
<div style="display: flex; gap: 12px; align-items: baseline"><span style="font-family: {MONO}; font-size: 12px; color: {MUTED}; flex: 0 0 44px">16:43</span><span style="font-size: 14px">Profil Empfohlen angewendet</span></div>
<div style="display: flex; gap: 12px; align-items: baseline"><span style="font-family: {MONO}; font-size: 12px; color: {MUTED}; flex: 0 0 44px">16:42</span><span style="font-size: 14px">Wiederherstellungspunkt erstellt</span></div>
</div>
<a href="#" style="font-size: 13px; color: {BLUEXL}; margin-top: auto">Ganzes Protokoll ansehen</a>
</section>
</div>'''

# ---------- Tweaks ----------
def chip(t, active=False):
    st = f"background: {BLUE}; color: #ffffff; border: 1px solid {BLUE}" if active else f"background: {BG2}; color: {TEXT2}; border: 1px solid {LINE}"
    return f'<button type="button" aria-pressed="{"true" if active else "false"}" style="{st}; border-radius: 999px; padding: 8px 14px; font-family: {SANS}; font-size: 13px; min-height: 36px; cursor: pointer">{t}</button>'

def tweak(title, desc, on, medium=False, hint="", msg="", wipe=False):
    border = BLUE if on else LINE
    hint_html = f'<span style="font-family: {MONO}; font-size: 11px; color: {MUTED}">{hint}</span>' if hint else ""
    msg_html = f'<span style="font-size: 12px; color: {OK}">{msg}</span>' if msg else ""
    # Pixel-Wisch: blaue Pixel laufen nach "Angewendet" über die Karte und verblassen
    squares = "".join(f'<span style="width: 10px; height: 10px; background: {BLUE}; opacity: {o}"></span>' for o in (0.9, 0.7, 0.5, 0.35, 0.2, 0.1))
    wipe_html = (f'<div aria-hidden="true" style="position: absolute; right: 92px; top: 50%; margin-top: -5px; display: flex; gap: 6px; flex-direction: row-reverse">{squares}</div>') if wipe else ""
    return f'''<div style="position: relative; overflow: hidden; background: {BG2}; border: 1px solid {border}; border-radius: 12px; padding: 16px 20px; display: flex; align-items: center; gap: 24px">
{wipe_html}
<div style="position: relative; flex: 1 1 auto; min-width: 0; display: flex; flex-direction: column; gap: 6px">
<div style="display: flex; flex-wrap: wrap; align-items: center; gap: 10px"><span style="font-weight: 600; font-size: 15px">{title}</span>{badge("Risiko mittel" if medium else "Risiko niedrig", medium)}{hint_html}</div>
<span style="font-size: 13px; color: {TEXT2}; line-height: 1.5">{desc}</span>
{msg_html}
</div>
<div style="position: relative">{toggle(on, title)}</div>
</div>'''

tweaks = f'''<div style="display: flex; flex-direction: column; gap: 8px">{label("TWEAKS")}{h1("Tweaks")}{p("Windows-Einstellungen für Leistung, Gaming, Oberfläche und Netzwerk. Jeder Tweak mit Erklärung, Risiko-Stufe und Rückgängig-Funktion.")}</div>
<div style="display: flex; flex-wrap: wrap; align-items: center; gap: 12px">
<label style="flex: 1 1 320px; max-width: 420px; display: flex; align-items: center; gap: 10px; background: {BG2}; border: 1px solid {LINE}; border-radius: 9px; padding: 0 14px; min-height: 44px">
<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="{MUTED}" stroke-width="2" aria-hidden="true"><circle cx="11" cy="11" r="7"></circle><line x1="21" y1="21" x2="16.5" y2="16.5"></line></svg>
<span style="position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0)">Tweaks durchsuchen</span>
<input type="search" placeholder="Tweaks durchsuchen …" style="flex: 1; background: transparent; border: none; outline: none; color: {TEXT}; font-family: {SANS}; font-size: 14px">
</label>
<div style="display: flex; flex-wrap: wrap; gap: 8px">{chip("Alle", True)}{chip("Risiko niedrig")}{chip("Risiko mittel")}{chip("Aktiv")}</div>
<span style="font-family: {MONO}; font-size: 12px; color: {MUTED}; margin-left: auto">Tweaks: 39 · aktiv: 7</span>
</div>
<section style="display: flex; flex-direction: column; gap: 10px">
{label("LEISTUNG")}
{tweak("Animationen reduzieren", "Schaltet die Animationen beim Minimieren und Maximieren, in der Taskleiste und die Desktop-Vorschau ab. Fenster reagieren spürbar direkter.", True, hint="Abmelden nötig")}
{tweak("Autostart-Verzögerung entfernen", "Windows wartet nach der Anmeldung einige Sekunden, bevor Autostart-Programme starten. Ohne Verzögerung sind sie schneller bereit.", True, msg="Angewendet ✓ Alter Zustand ist gesichert.", wipe=True)}
{tweak("Energieplan Ultimative Leistung", "Der Prozessor bleibt auf hohem Takt und wechselt nicht in Stromsparzustände. Weniger Mikroruckler, aber höherer Stromverbrauch.", False, medium=True)}
</section>
<section style="display: flex; flex-direction: column; gap: 10px">
{label("GAMING")}
{tweak("Mausbeschleunigung ausschalten", "Der Mauszeiger bewegt sich immer gleich weit, egal wie schnell du die Maus bewegst. Wichtig für genaues Zielen in Shootern.", False, hint="Abmelden nötig")}
{tweak("Hardwarebeschleunigte GPU-Planung", "Die Grafikkarte verwaltet ihren Arbeitsspeicher selbst. Kann die Latenz senken und ist für DLSS Frame Generation nötig.", False, medium=True, hint="Neustart nötig")}
</section>'''

# ---------- Wiederherstellung ----------
def rp_row(date, desc):
    return f'<div style="display: grid; grid-template-columns: 170px minmax(0, 1fr); gap: 16px; padding: 11px 0; border-top: 1px solid {LINE}"><span style="font-family: {MONO}; font-size: 12px; color: {MUTED}">{date}</span><span style="font-size: 14px">{desc}</span></div>'

def log_row(time, text, ok=True):
    dot = OK if ok else "#ff5c5c"
    return f'<div style="display: grid; grid-template-columns: 16px 120px minmax(0, 1fr); gap: 12px; align-items: baseline; padding: 8px 0"><span style="width: 6px; height: 6px; background: {dot}; display: inline-block"></span><span style="font-family: {MONO}; font-size: 12px; color: {MUTED}">{time}</span><span style="font-size: 14px; color: {TEXT if ok else "#ff8a8a"}">{text}</span></div>'

def active_row(title, since):
    return f'<div style="display: flex; align-items: center; gap: 12px; padding: 10px 0; border-top: 1px solid {LINE}"><span style="flex: 1; font-size: 14px">{title}</span><span style="font-family: {MONO}; font-size: 12px; color: {MUTED}">{since}</span><button type="button" style="border: 1px solid {LINE}; border-radius: 7px; background: {BG3}; color: {TEXT}; font-family: {SANS}; font-size: 12px; padding: 6px 10px; min-height: 32px; cursor: pointer">Zurücksetzen</button></div>'

restore = f'''<div style="display: flex; flex-direction: column; gap: 8px">{label("WIEDERHERSTELLUNG")}{h1("Wiederherstellung")}{p("Drei Sicherungsebenen: Windows-Wiederherstellungspunkt, Journal mit jedem alten Wert und Registry-Export als .reg-Datei.")}</div>
<div style="display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 16px">
{stat("SYSTEMSCHUTZ", "Aktiv", "Laufwerk C: geschützt", OK)}
{stat("WIEDERHERSTELLUNGSPUNKTE", "3", "letzter heute, 16:42 Uhr", TEXT)}
{stat("REGISTRY-SICHERUNGEN", "12", ".reg-Dateien im Sicherungsordner", TEXT)}
</div>
<section style="{CARD}; padding: 20px 22px; display: flex; flex-direction: column; gap: 12px">
<div style="display: flex; flex-wrap: wrap; justify-content: space-between; align-items: center; gap: 12px">{h2("Wiederherstellungspunkte")}<div style="display: flex; flex-wrap: wrap; gap: 10px">{btn("Jetzt Punkt erstellen")}{btn("Windows-Systemwiederherstellung", False)}</div></div>
<div>
{rp_row("04.11.2026, 16:42", "Munz Quick Tweaks: vor Änderungen")}
{rp_row("03.11.2026, 09:15", "Windows Update")}
{rp_row("28.10.2026, 20:03", "Installation: NVIDIA Grafiktreiber")}
</div>
</section>
<div style="display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 16px">
<section style="{CARD}; padding: 20px 22px; display: flex; flex-direction: column; gap: 12px">
<div style="display: flex; justify-content: space-between; align-items: center; gap: 12px">{h2("Aktive Tweaks")}{btn("Alle zurücksetzen", False)}</div>
<div>
{active_row("Dateiendungen anzeigen", "16:44")}
{active_row("Autostart-Verzögerung entfernen", "16:43")}
{active_row("Werbe-ID ausschalten", "16:43")}
</div>
</section>
<section style="{CARD}; padding: 20px 22px; display: flex; flex-direction: column; gap: 6px">
<div style="display: flex; justify-content: space-between; align-items: center; gap: 12px">{h2("Änderungsprotokoll")}<a href="#" style="font-size: 13px; color: {BLUEXL}">Sicherungsordner öffnen</a></div>
{log_row("Heute 16:44", "Angewendet: Dateiendungen anzeigen")}
{log_row("Heute 16:43", "Profil Empfohlen angewendet")}
{log_row("Heute 16:43", "Registry gesichert: privacy.adid.off")}
{log_row("Heute 16:42", "Wiederherstellungspunkt erstellt")}
{log_row("Gestern 21:10", "Fehlgeschlagen: Recall ausschalten (Zugriff verweigert)", False)}
</section>
</div>'''

files = {
 "Main.dc.html": page("Dashboard", "Dashboard", dash, 900),
 "Tweaks.dc.html": page("Tweaks", "Tweaks", tweaks, 960),
 "Wiederherstellung.dc.html": page("Wiederherstellung", "Wiederherstellung", restore, 900),
}
for name, html in files.items():
    open(os.path.join(ROOT, name), "w", encoding="utf-8").write(html)

now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
canvas = {"v":3,"createdOnFiles":{"v":1,"at":now},"title":"Munz Quick Tweaks – Mockups Phase 6","launch":{"view":"canvas"},"pages":[],
 "boards":{
   "Main.dc.html":{"x":0,"y":0,"w":1280,"h":900,"title":"Dashboard","expand":"fill"},
   "Tweaks.dc.html":{"x":1360,"y":0,"w":1280,"h":960,"title":"Tweaks","expand":"fill"},
   "Wiederherstellung.dc.html":{"x":2720,"y":0,"w":1280,"h":900,"title":"Wiederherstellung","expand":"fill"}},
 "order":["Main.dc.html","Tweaks.dc.html","Wiederherstellung.dc.html"],
 "notes":{"t1":{"x":0,"y":-300,"text":"Munz Quick Tweaks · Entwürfe Phase 6","kind":"title1","maxW":4000}},
 "designSystems":[]}
open(os.path.join(ROOT, "canvas.json"), "w", encoding="utf-8").write(json.dumps(canvas, ensure_ascii=False, indent=2))
print("ok")
