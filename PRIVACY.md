# 隐私政策 / Privacy Policy

拾字 · SnapText 是一个本地文字识别工具。

## 收集的数据

本应用不收集、不上传、不出售任何个人数据。

具体来说：

- 截图和图片只在你的电脑内存中处理；
- 不向任何服务器发送图片、截图或识别结果；
- 不包含用户行为统计、崩溃上报或广告 SDK；
- 不读取与识别无关的文件。

## 本地处理

识别使用以下本地组件：

- PP-OCRv5 / ONNX Runtime：默认高精度识别；
- Windows.Media.Ocr：快速模式和回退识别。

模型文件随应用分发，不需要联网下载。

## 剪贴板

只有在你点击「从剪贴板」时，应用才读取剪贴板中的图片。剪贴板内容不会被上传或写入日志。

## 本地日志

应用可能在 `%LOCALAPPDATA%\SnapText\snaptext.log` 写入错误日志，用于排查问题。日志不包含截图或图片内容。

## 删除数据

退出应用后，识别图片不会被保留。设置文件保存在 `%LOCALAPPDATA%\SnapText\settings.json`，可以随时删除。

## 联系方式

通过 GitHub Issues 联系：

https://github.com/fangtunan/snaptext/issues

---

This application performs OCR entirely on the local computer. It does not
collect, transmit, or sell personal data, and it does not upload screenshots
or images to any server.
