# StepReplay

基于 WinUI 3 的简单工具，录制鼠标和键盘操作并回放。

## 功能

- 全局记录鼠标移动、点击、滚轮和键盘按下/抬起。
- 自动忽略本程序窗口内的鼠标/键盘操作，避免把“开始/停止/回放”按钮录进去。
- 按录制时的时间间隔用 `SendInput` 回放。
- 支持设置回放次数，按同一段记录连续回放多次。
- 支持回放中取消、清空记录。
- 导出为 `json` 文件，或从 `json` 文件导入记录。

## 运行

```powershell
dotnet build .\StepReplay.sln -c Debug -p:Platform=x64
dotnet run --project .\StepReplay\StepReplay.csproj -p:Platform=x64
```

> 注意：回放会真实控制当前桌面输入。请先在安全场景测试；如果要操作管理员权限窗口，程序也需要以管理员身份运行。
