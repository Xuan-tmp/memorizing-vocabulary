$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path "$PSScriptRoot\dist" | Out-Null
if (!(Test-Path "$PSScriptRoot\assets\placeholder.ico")) { & "$PSScriptRoot\assets\generate-icon.ps1" }
$iconArgument = '/win32icon:' + $PSScriptRoot + '\assets\placeholder.ico'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe /codepage:65001 $iconArgument /out:"$PSScriptRoot\dist\单词抽背.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /reference:System.Core.dll "$PSScriptRoot\App.cs"
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
$resourceArgument = '/resource:' + $PSScriptRoot + '\dist\单词抽背.exe,app'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe /codepage:65001 $iconArgument /out:"$PSScriptRoot\dist\单词抽背安装程序.exe" /reference:System.Windows.Forms.dll $resourceArgument "$PSScriptRoot\Setup.cs"
if ($LASTEXITCODE -ne 0) { throw '安装程序编译失败' }
