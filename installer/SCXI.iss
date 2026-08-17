#define MyAppName "SCXI"
#define MyAppVersion "0.1.0"
#define MyAppExeName "SCXI.exe"

#define UsbipInstaller "USBip-0.9.7.7-x64.exe"


; =====================================================================
; SCXI INSTALLER
; =====================================================================

[Setup]

AppId={{6F0442FC-2742-4C9D-9AB5-62A592965F05}

AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=SCXI Project

VersionInfoVersion=0.1.0.0

DefaultDirName={userpf}\SCXI
DefaultGroupName=SCXI

UninstallDisplayName=SCXI
UninstallDisplayIcon={app}\SCXI.exe

SetupIconFile={#SourcePath}\..\Assets\scxi-enabled.ico

OutputDir={#SourcePath}\output
OutputBaseFilename=SCXI-Setup-{#MyAppVersion}-win-x64

Compression=lzma2
SolidCompression=yes

WizardStyle=modern

PrivilegesRequired=lowest

SetupArchitecture=x64
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os

DisableProgramGroupPage=yes

CloseApplications=yes
RestartApplications=no

RestartIfNeededByRun=yes


; =====================================================================
; FILES
; =====================================================================

[Files]

; ---------------------------------------------------------------------
; MAIN SCXI EXECUTABLE
; ---------------------------------------------------------------------

Source: "{#SourcePath}\..\dist\SCXI-win-x64\SCXI.exe"; \
    DestDir: "{app}"; \
    Flags: ignoreversion


; ---------------------------------------------------------------------
; BUNDLED VIIPER SERVER
; ---------------------------------------------------------------------

Source: "{#SourcePath}\..\dist\SCXI-win-x64\tools\viiper\viiper.exe"; \
    DestDir: "{app}\tools\viiper"; \
    Flags: ignoreversion


; ---------------------------------------------------------------------
; THIRD-PARTY LICENSES / SOURCE
; ---------------------------------------------------------------------

Source: "{#SourcePath}\licenses\VIIPER-GPL-3.0.txt"; \
    DestDir: "{app}\licenses"; \
    Flags: ignoreversion


Source: "{#SourcePath}\licenses\VIIPER-v0.7.0-source.zip"; \
    DestDir: "{app}\licenses"; \
    Flags: ignoreversion


Source: "{#SourcePath}\licenses\USBIP-WIN2-BSD-2-Clause.txt"; \
    DestDir: "{app}\licenses"; \
    Flags: ignoreversion


; ---------------------------------------------------------------------
; USBIP-WIN2 0.9.7.7 INSTALLER
;
; This dependency installer is only extracted when USBip isn't already
; installed.
; ---------------------------------------------------------------------

Source: "{#SourcePath}\dependencies\{#UsbipInstaller}"; \
    DestDir: "{tmp}"; \
    Flags: deleteafterinstall signcheck; \
    Check: NeedUsbipInstall


; =====================================================================
; REGISTRY
; =====================================================================

[Registry]

; ---------------------------------------------------------------------
; START WITH WINDOWS CLEANUP
;
; SCXI creates this value itself when the user enables:
;
;     Start with Windows
;
; Setup does not create it. The uninstaller removes it if present.
; ---------------------------------------------------------------------

Root: HKCU; \
    Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: none; \
    ValueName: "SCXI"; \
    Flags: uninsdeletevalue dontcreatekey


; =====================================================================
; START MENU
; =====================================================================

[Icons]

Name: "{group}\SCXI"; \
    Filename: "{app}\SCXI.exe"; \
    WorkingDir: "{app}"


; =====================================================================
; RUN
; =====================================================================

[Run]

; ---------------------------------------------------------------------
; USBIP-WIN2
;
; SCXI Setup remains unelevated.
;
; Windows elevates only the USBip driver installer when required.
; ---------------------------------------------------------------------

Filename: "{tmp}\{#UsbipInstaller}"; \
    Parameters: "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010 /TYPE=compact"; \
    WorkingDir: "{tmp}"; \
    StatusMsg: "Installing the USB/IP virtual device driver..."; \
    Verb: "runas"; \
    Flags: shellexec waituntilterminated; \
    Check: NeedUsbipInstall


; ---------------------------------------------------------------------
; LAUNCH SCXI
; ---------------------------------------------------------------------

Filename: "{app}\SCXI.exe"; \
    Description: "Launch SCXI"; \
    WorkingDir: "{app}"; \
    Flags: nowait postinstall skipifsilent; \
    Check: CanLaunchImmediately


; =====================================================================
; CODE
; =====================================================================

[Code]

var
    UsbipWasMissingAtStart: Boolean;
    UsbipWarningAccepted: Boolean;


// =====================================================================
// USB/IP DETECTION
// =====================================================================

function UsbipInstalled: Boolean;
begin

    Result :=
        FileExists(
            ExpandConstant(
                '{commonpf64}\USBip\usbip.exe'
            )
        );

end;


// =====================================================================
// INITIALIZATION
// =====================================================================

function InitializeSetup: Boolean;
begin

    UsbipWasMissingAtStart :=
        not UsbipInstalled;


    UsbipWarningAccepted :=
        False;


    Result :=
        True;

end;


// =====================================================================
// USBIP INSTALL CHECK
// =====================================================================

function NeedUsbipInstall: Boolean;
begin

    Result :=
        UsbipWasMissingAtStart;

end;


// =====================================================================
// USB DEVICE WARNING
// =====================================================================

function NextButtonClick(
    CurPageID: Integer
): Boolean;

var
    Choice: Integer;

begin

    Result :=
        True;


    if
        (CurPageID = wpReady) and
        UsbipWasMissingAtStart and
        (not UsbipWarningAccepted) and
        (not WizardSilent)
    then
    begin

        Choice :=
            MsgBox(
                'SCXI requires the USB/IP virtual device driver.' + #13#10 + #13#10 +
                'Installing this driver temporarily restarts USB 3.0 hubs.' + #13#10 +
                'USB devices such as keyboards, mice, audio interfaces, ' +
                'webcams, and USB drives may briefly disconnect.' + #13#10 + #13#10 +
                'Save any important work involving USB devices before continuing.' + #13#10 + #13#10 +
                'Windows will ask for administrator permission only for the ' +
                'USB/IP driver installation.' + #13#10 + #13#10 +
                'Continue installing SCXI?',
                mbConfirmation,
                MB_YESNO or MB_DEFBUTTON2
            );


        if Choice = IDYES then
        begin

            UsbipWarningAccepted :=
                True;

        end
        else
        begin

            Result :=
                False;

        end;

    end;

end;


// =====================================================================
// RESTART
// =====================================================================

function NeedRestart: Boolean;
begin

    Result :=
        UsbipWasMissingAtStart;

end;


// =====================================================================
// IMMEDIATE LAUNCH CHECK
// =====================================================================

function CanLaunchImmediately: Boolean;
begin

    Result :=
        not UsbipWasMissingAtStart;

end;
