import io

p = 'README.md'
s = io.open(p, encoding='utf-8-sig').read()

start = s.index('## 下载')
end = s.index('## 功能')
new = '''## 下载

**[下载最新版](https://github.com/yyunx/wantodo/releases/latest)** — Windows 10 1809+ / 11 x64，两个包任选其一：

| 包 | 大小 | 需要装 .NET 吗 | 适合 |
| --- | --- | --- | --- |
| `wantodo-0.5.0-win-x64.zip` | 60.9 MB | 不需要 | 直接发给别人，解压双击即用 |
| `wantodo-0.5.0-fdd-win-x64.zip` | 0.2 MB | 需要 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) | 自己机器已经装了运行时，想省空间 |

用法：解压 → 双击 `Wantodo.exe`。第一次运行如果被 SmartScreen 拦下（exe 没有代码签名），点「更多信息 → 仍要运行」。

想要开始菜单快捷方式，把 `install.ps1` 和 `Wantodo.exe` 放一起，执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\\install.ps1
```

它只装到 `%LOCALAPPDATA%\\Programs\\wantodo`，不需要管理员，也不碰系统目录。

---

'''
s = s[:start] + new + s[end:]

io.open(p, 'w', encoding='utf-8-sig', newline='').write(s)
print('ok')
