# -*- coding: utf-8 -*-
import json, sys, urllib.request, urllib.parse, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
HDRS = {"User-Agent": UA, "Referer": "https://y.qq.com/"}

def req(url, headers=None, data=None, method=None, timeout=30):
    h = dict(HDRS)
    if headers: h.update(headers)
    r = urllib.request.Request(url, data=data, headers=h, method=method)
    try:
        with urllib.request.urlopen(r, timeout=timeout) as resp:
            return resp.status, dict(resp.headers), resp.read()
    except urllib.error.HTTPError as e:
        return e.code, dict(e.headers), e.read()
    except Exception as e:
        return None, {}, str(e).encode()

def show(tag, status, body, n=1200):
    print("=" * 70)
    print("### " + tag)
    print("STATUS:", status)
    if isinstance(body, bytes):
        txt = body.decode('utf-8', 'replace')
    else:
        txt = str(body)
    print("LEN:", len(txt))
    print(txt[:n])
    print()

if __name__ == "__main__":
    pass
