[Setup]
AppName=单词抽背
AppVersion=1.0.0
SetupIconFile=assets\placeholder.ico
DefaultDirName={localappdata}\Programs\单词抽背
DisableDirPage=no
PrivilegesRequired=lowest
OutputDir=installer
OutputBaseFilename=单词抽背安装程序
UninstallDisplayIcon={app}\单词抽背.exe
[Files]
Source: "dist\单词抽背.exe"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{userprograms}\单词抽背"; Filename: "{app}\单词抽背.exe"
Name: "{userdesktop}\单词抽背"; Filename: "{app}\单词抽背.exe"
[Run]
Filename: "{app}\单词抽背.exe"; Description: "启动单词抽背"; Flags: nowait postinstall skipifsilent
