; Instalador do Liquid Glass Dock (Inno Setup 6).
;
; Gerado por "dotnet publish src/LiquidGlass -p:PublishProfile=Installer", que define AppVersion e
; PublishDir. Instala por usuário, sem pedir permissão de administrador.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #error PublishDir não definido: gere o instalador pelo "dotnet publish" do projeto.
#endif

#define AppName "Liquid Glass Dock"
#define AppExe "LiquidGlass.exe"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#define RunValue "LiquidGlassDock"

[Setup]
; O AppId identifica o app entre versões: nunca altere.
AppId={{6C73E629-05B2-4218-A7E8-76A2C5AB4A31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=PedroRuedas
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputBaseFilename=LiquidGlassDock-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; A dock é encerrada pelo próprio instalador (veja [Code]); o Gerenciador de Reinicialização não é necessário.
CloseApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "startup"; Description: "Iniciar com o Windows (a dock fica em segundo plano)"; GroupDescription: "Opções:"
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Opções:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Atualizações substituem a publicação inteira: arquivos de versões antigas não ficam para trás.
Type: filesandordirs; Name: "{app}\*"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Abre a dock (ou a personalização, se ela já estiver aberta)"
Name: "{autoprograms}\Encerrar {#AppName}"; Filename: "{app}\{#AppExe}"; Parameters: "--quit"; Comment: "Fecha a dock e devolve a barra de tarefas do Windows"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Mesmo valor que a opção "Iniciar com o Windows" da personalização usa.
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#RunValue}"; ValueData: """{app}\{#AppExe}"""; Tasks: startup
Root: HKCU; Subkey: "{#RunKey}"; ValueName: "{#RunValue}"; Flags: deletevalue; Tasks: not startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir o {#AppName} agora"; Flags: nowait postinstall skipifsilent
; Atualização pela própria dock (silenciosa, com /RELAUNCH=1): a dock volta sozinha no fim.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: RelaunchRequested

[Code]
const
  QuitSignal = 'Local\LiquidGlass.Dock.Quit';
  InstanceMutex = 'Local\LiquidGlass.Dock';
  EventModifyState = $0002;
  Synchronize = $00100000;
  QuitTimeoutMs = 10000;

function OpenEvent(Access: DWORD; Inherit: BOOL; Name: string): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Handle: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function OpenMutex(Access: DWORD; Inherit: BOOL; Name: string): THandle;
  external 'OpenMutexW@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

{ Pede a qualquer dock em execução (instalada ou não) que encerre e espera ela devolver a barra
  de tarefas. Funciona mesmo na primeira instalação, quando ainda não há executável instalado. }
procedure QuitRunningDock();
var
  Signal, Mutex: THandle;
  Waited: Integer;
begin
  Signal := OpenEvent(EventModifyState, False, QuitSignal);
  if Signal = 0 then
    Exit;
  SetEvent(Signal);
  CloseHandle(Signal);

  Waited := 0;
  repeat
    Sleep(200);
    Waited := Waited + 200;
    Mutex := OpenMutex(Synchronize, False, InstanceMutex);
    if Mutex <> 0 then
      CloseHandle(Mutex);
  until (Mutex = 0) or (Waited >= QuitTimeoutMs);
end;

function RelaunchRequested(): Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  QuitRunningDock();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    { "--quit" encerra a dock e, mesmo sem dock aberta, restaura uma barra de tarefas que tenha
      ficado escondida por um encerramento forçado. Nunca deixa o usuário sem barra. }
    if not Exec(ExpandConstant('{app}\{#AppExe}'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      QuitRunningDock();
  end
  else if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, '{#RunKey}', '{#RunValue}');
end;
