# -*- coding: utf-8 -*-
import json, sys, base64, urllib.request, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

def api(path):
    r = urllib.request.Request("https://api.github.com/" + path,
                               headers={"User-Agent": "research", "Accept": "application/vnd.github+json"})
    with urllib.request.urlopen(r, timeout=45) as resp:
        return json.loads(resp.read().decode("utf-8"))

def getfile(repo, path):
    d = api("repos/%s/contents/%s" % (repo, path))
    if isinstance(d, list):
        return "\n".join(x["name"] for x in d)
    if d.get("encoding") == "base64":
        return base64.b64decode(d["content"]).decode("utf-8", "replace")
    return d.get("content", "")

if __name__ == "__main__":
    repo, path = sys.argv[1], sys.argv[2]
    print(getfile(repo, path))
