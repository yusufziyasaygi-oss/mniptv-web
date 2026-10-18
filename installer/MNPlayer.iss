#define AppName "MN Player"
#define AppVersion "2.2.4"
#define AppExe "MNPlayer.exe"

[Setup]
AppId={{8B2F6D1A-4C0E-4E7B-9A53-6D1F0C2B7A94}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Mavi Nokta
DefaultDirName={localappdata}\Programs\MN Player
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\out
OutputBaseFilename=MN-Player-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
ChangesAssociations=yes

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstüne kısayol oluştur"; GroupDescription: "Kısayollar:"
Name: "preview"; Description: "Explorer'da Ctrl+M ile seçili videoyu önizle (arka planda küçük yardımcı çalışır)"; GroupDescription: "Önizleme:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: filesandordirs; Name: "{app}\*"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; "Birlikte aç" listesi ve Windows Ayarlar > Varsayılan uygulamalar için kayıt (varsayılanı Windows kullanıcıya sordurur).
Root: HKA; Subkey: "Software\Classes\MNPlayer.Media"; ValueType: string; ValueName: ""; ValueData: "MN Player ile açılan medya"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\MNPlayer.Media\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"
Root: HKA; Subkey: "Software\Classes\MNPlayer.Media\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "MN Player"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\MN Player\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "MN Player"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\MN Player\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Mavi Nokta video oynatıcı"
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "MN Player"; ValueData: "Software\MN Player\Capabilities"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\mnplayer"; ValueType: string; ValueName: ""; ValueData: "URL:MN Player"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\mnplayer"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\mnplayer\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MNPlayerPreview"; ValueData: """{app}\{#AppExe}"" --preview-service"; Flags: uninsdeletevalue; Tasks: preview
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mp4"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mp4"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.mp4\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".m4v"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".m4v"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.m4v\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mov"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mov"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.mov\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mkv"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mkv"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.mkv\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".avi"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".avi"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.avi\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".webm"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".webm"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.webm\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".wmv"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wmv"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.wmv\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".flv"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".flv"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.flv\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".f4v"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".f4v"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.f4v\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mpg"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mpg"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.mpg\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mpeg"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mpeg"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.mpeg\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mpe"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mpe"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.mpe\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".m1v"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".m1v"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.m1v\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".m2v"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".m2v"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.m2v\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mpv"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mpv"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.mpv\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".ts"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ts"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.ts\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mts"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mts"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.mts\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".m2ts"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".m2ts"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.m2ts\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".m2t"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".m2t"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.m2t\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".vob"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".vob"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.vob\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".ogv"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ogv"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.ogv\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".ogm"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ogm"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.ogm\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".3gp"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".3gp"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.3gp\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".3g2"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".3g2"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.3g2\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".asf"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".asf"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.asf\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".divx"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".divx"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.divx\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".rm"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".rm"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.rm\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".rmvb"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".rmvb"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.rmvb\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".dv"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".dv"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.dv\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mxf"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mxf"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.mxf\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".nut"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".nut"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.nut\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".nsv"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".nsv"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.nsv\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".hevc"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".hevc"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.hevc\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".h264"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".h264"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.h264\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".h265"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".h265"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.h265\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".y4m"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".y4m"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.y4m\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".av1"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".av1"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.av1\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".amv"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".amv"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.amv\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mpegts"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mpegts"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.mpegts\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mp3"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mp3"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".m4a"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".m4a"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.m4a\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".aac"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".aac"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.aac\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".wav"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wav"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".flac"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".flac"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.flac\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".ogg"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ogg"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.ogg\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".opus"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".opus"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.opus\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".aiff"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".aiff"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.aiff\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".aif"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".aif"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.aif\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".wma"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wma"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.wma\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".ac3"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ac3"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.ac3\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".dts"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".dts"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.dts\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".ape"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ape"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.ape\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".au"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".au"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.au\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mka"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mka"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.mka\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\MNPlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".alac"; ValueData: ""
Root: HKA; Subkey: "Software\MN Player\Capabilities\FileAssociations"; ValueType: string; ValueName: ".alac"; ValueData: "MNPlayer.Media"
Root: HKA; Subkey: "Software\Classes\.alac\OpenWithProgids"; ValueType: string; ValueName: "MNPlayer.Media"; ValueData: ""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--preview-service"; Description: "Önizleme yardımcısını başlat"; Flags: nowait runhidden skipifsilent; Tasks: preview
Filename: "{app}\{#AppExe}"; Description: "MN Player'ı başlat"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM MNPlayer.exe"; Flags: runhidden; RunOnceId: "KillMNPlayer"
