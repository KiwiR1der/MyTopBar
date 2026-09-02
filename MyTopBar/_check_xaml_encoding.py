from pathlib import Path

p = Path(r"E:\06IEMKatowice\WPFProjects\MyTopBar\MyTopBar\MainWindow.xaml")
data = p.read_bytes()
print("has utf8 bom", data[:3] == b"\xef\xbb\xbf")
print("file size", len(data))

start = 0
while True:
    i = data.find(b'Content="', start)
    if i < 0:
        break
    j = data.find(b'"', i + 9)
    raw = data[i : j + 1]
    print("raw bytes", list(raw))
    print("hex", raw.hex())
    print("utf8", raw.decode("utf-8", errors="replace"))
    try:
        print("gbk", raw.decode("gbk", errors="replace"))
    except Exception as e:
        print("gbk error", e)
    start = j + 1
