# 全能自定义文件夹图标

给 Windows 文件夹换上自己选的照片。选择文件夹、选择或粘贴图片、点击“应用”即可。

程序自动校正图片方向、保持比例，并生成 Windows 所需的多尺寸 ICO。不需要提前把照片改成正方形，也不用自己转换格式。所有转换都在本机完成。

## 使用

1. 解压便携包，双击 `全能自定义文件夹图标.exe`。
2. 选择目标文件夹。地址可直接粘贴到输入框，再按 Enter 或点“确认路径”。
3. 选择图片、拖入图片，或截图后点“粘贴图片”。在预览区按 Ctrl+V 也可粘贴截图。
4. 查看预览，点击“应用”。

默认勾选“完整图片（自动适配）”，横图和竖图都会完整显示，空余区域透明。

选择图片后，可点“左转 90°”或“右转 90°”调整方向。预览和最终图标使用相同方向。旋转后裁剪框会重置，可重新框选；原图片保持不变。

粘贴的图片同样可以旋转、裁剪和应用，无需先保存截图。光标在地址等文本框内时，Ctrl+V 保持普通文本粘贴；点击“粘贴图片”按钮可直接读取剪贴板中的图片。程序不会修改剪贴板，仅在你主动粘贴时读取图片。粘贴图片的临时副本在换图或正常关闭窗口时清理。

需要裁剪时，点击“自定义裁剪 / 重新框选”，在左侧图片上拖出想要的区域。拖动框内可移动选区，拖动边角或边中间的小方块可调整大小。右侧实时显示最终图标，点击“应用到文件夹”才会写入。

默认允许任意长宽比例。勾选“锁定正方形（1:1）”可固定比例；“重置裁剪”恢复居中正方形。按 Esc 可取消正在进行的拖动。长方形选区会等比放进图标，空余区域透明，不拉伸图片。

每个图标包含 16、24、32、48、64、128、256 像素七种尺寸，Windows 按显示需要选择。无需手动选择一个“兼容尺寸”。资源管理器中的显示大小仍由其“查看”设置控制；图标未立即更新时可按 F5。

点击“撤销上次替换”可以逐次恢复。请保留程序旁的 `data/backups`，其中保存原图标、原 `desktop.ini` 及属性。程序不会修改作为输入的原照片。

## 图片格式

| 格式 | 处理方式 |
| --- | --- |
| JPG / JPEG、PNG、BMP、GIF、TIFF、ICO | 内置解码；动画和多页图片取静态画面，ICO 优先较大尺寸 |
| WebP、AVIF、HEIC 等 | 使用已安装的 Windows 图像解码器 |
| 其他格式 | 尝试系统解码器；无法解码时说明原因 |

本程序不承诺解码损坏、加密或缺少解码器的文件。这种情况会停止操作，保留当前图标。自动适配能处理比例和尺寸，但不能为低清图片补出原本不存在的细节。

## 运行要求与备份

- Windows 10 / 11，.NET Framework 4.8。
- 将便携包解压到可写目录，不要直接在 ZIP 中运行。
- 一般不需要管理员权限；目标目录必须允许当前用户修改。
- 不创建开机启动项、后台服务或系统右键菜单。
- 不支持驱动器根目录、重解析目录等特殊目标。
- 使用 .NET Framework 路径接口，请避免超过 Windows 传统路径长度限制。

应用时只维护目标中的 `.folder-portrait.ico` 和 `desktop.ini`，并设置 Windows 识别图标所需的属性。其他文件保留不动。首次及后续替换都保存备份。

撤销前会检查图标和配置是否被其他程序改动。有冲突时会停止，避免覆盖别人的修改。请不要在程序运行中移动或删除备份。

## 命令行与自动化

程序带命令行入口，不打开图形界面。自动化建议指定 `--result`，读取 JSON 结果及进程退出码。

```powershell
$exe = Join-Path $PWD '全能自定义文件夹图标.exe'
$process = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -Wait -ArgumentList @(
    'apply',
    '--folder', '"D:\Pictures\示例文件夹"',
    '--image', '"D:\Pictures\照片.jpg"',
    '--backups', '"D:\IconBackups"',
    '--result', '"D:\IconResults\apply-001.json"'
)
$process.ExitCode
Get-Content -LiteralPath 'D:\IconResults\apply-001.json' -Raw -Encoding UTF8
```

命令：

```text
apply --folder <目录> --image <图片> [--rotate 0|90|180|270] [--fit contain|cover] [--crop x,y,宽,高]
undo  --folder <目录>
```

两个命令都接受 `--backups <备份目录>` 和 `--result <新建JSON文件>`。

- 默认 `contain`：完整图片等比适配。
- `cover`：居中裁成正方形；指定 `--crop x,y,宽,高` 可自由裁剪，再等比适配图标。
- `--rotate` 按顺时针旋转，默认 `0`。先校正照片方向，再执行手动旋转。
- 兼容旧的 `--crop x,y,边长` 正方形格式。裁剪坐标基于校正方向并完成手动旋转后的图片。
- `contain` 不能与 `--crop` 同用。
- 结果文件必须是新文件，父目录须存在，且不能位于目标文件夹内。
- 撤销时须指定应用时使用的同一个备份目录。
- 退出码：`0` 成功，`1` 失败，`2` 图标操作已完成但结果输出失败。

## 从源码构建

不需要第三方 NuGet 包。使用 Windows 自带的 .NET Framework C# 编译器：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

`build.ps1` 在 `release` 生成程序。`test.ps1` 使用合成图片和隔离目录，检查格式解码、自动适配、替换、撤销、冲突保护、图片粘贴、隐藏界面和命令行。不会打开可见窗口、读取个人照片或访问实际剪贴板。

`package.ps1` 在 `dist` 生成便携 ZIP 和源码 ZIP。打包采用明确的文件清单，不会带入使用记录、备份或测试图片。`data`、`release`、`dist` 及测试输出已加入 `.gitignore`。

测试细节见 [tests/README.md](tests/README.md)。
