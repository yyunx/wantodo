import io, re

p = 'README.md'
s = io.open(p, encoding='utf-8-sig').read()

# 1) 去掉正文里那几条「- ![image-...]」内嵌行
s = re.sub(r'^- !\[image-\d+\]\(docs/verify/image-\d+\.png\)\n', '', s, flags=re.M)
s = re.sub(r'^- !\[image-\d+\]\(docs/verify/image-\d+\.png\)\r?\n', '', s, flags=re.M)

# 2) 在「三个容易踩的点」之前插一个截图区
gallery = """## 截图

**工具栏**（加栏 / 卡住 / 随心记）

![工具栏](docs/verify/image-20261110233021915.png)

**卡住清单**

![卡住清单](docs/verify/image-20261110233046114.png)

**主界面悬停看卡住原因**

![悬停提示](docs/verify/image-20261110233342547.png)

**托盘菜单**

![托盘菜单](docs/verify/image-20261110233119314.png)

---

## 三个容易踩的点
"""

assert '## 三个容易踩的点' in s
s = s.replace('## 三个容易踩的点', gallery, 1)

io.open(p, 'w', encoding='utf-8-sig', newline='').write(s)
print('ok')
