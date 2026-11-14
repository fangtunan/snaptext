# 隐私政策 / Privacy Policy

拾字 · SnapText 是一个以本地 OCR 为主的截图取字工具。翻译模式默认关闭。

## 收集的数据

本应用不包含用户行为统计、崩溃上报或广告 SDK，也不读取与识别无关的文件。

- 截图和图片只在你的电脑内存中处理；
- OCR 识别完全在本机完成；
- 不向任何服务器发送图片、截图或剪贴板图片；
- 翻译模式默认关闭，开启后仅发送识别出的文字（见下文）。

## 本地处理

OCR 使用以下本地组件：

- PP-OCRv5 / ONNX Runtime：默认高精度识别；
- Windows.Media.Ocr：快速模式和回退识别。

模型文件随应用分发，不需要联网下载。

## 在线翻译（可选）

只有当你手动开启「翻译」复选框，并确认首次使用提示后，应用才会调用
[MyMemory](https://mymemory.translated.net/) 在线翻译服务：

- 发送内容：识别出的文字，不包含图片、截图、剪贴板图片或文件名；
- 目的：生成中文或英文译文；
- 接收方：MyMemory（`api.mymemory.translated.net`）；
- 触发时机：每次开启翻译后执行 OCR 或重新识别时；
- 关闭「翻译」后，应用不会再发送文字。

MyMemory 的可用性、免费额度和数据处理方式受其自身条款约束。翻译失败时，
应用会保留原文，不会把失败内容写入日志。

## 剪贴板

只有在你点击「从剪贴板」时，应用才读取剪贴板中的图片。剪贴板内容不会被上传或写入日志。

## 本地日志

应用可能在 `%LOCALAPPDATA%\SnapText\snaptext.log` 写入错误日志，用于排查问题。日志不包含截图、图片或识别文字。

## 删除数据

退出应用后，识别图片不会被保留。设置文件保存在 `%LOCALAPPDATA%\SnapText\settings.json`，可以随时删除。

## 联系方式

通过 GitHub Issues 联系：

https://github.com/fangtunan/snaptext/issues

---

OCR and screenshots are processed locally. The optional translation mode, when
explicitly enabled, sends only the recognized text to MyMemory
(`api.mymemory.translated.net`); it never uploads screenshots, images, clipboard
images, or file names. Translation is disabled by default.
