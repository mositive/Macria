# Starts Macria in automation mode and checks that no window of it is ever on
# the screen, then that it closes itself cleanly. Usage: gizli-duman.ps1 <Macria bin>
param([string]$Bin)
Add-Type -Namespace W3 -Name U -MemberDefinition @'
public delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, System.IntPtr l);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
public struct RECT { public int L, T, R, B; }
'@
$env:MACRIA_OTOMASYON = '1'
$env:MACRIA_OTOMASYON_KAPAN = '6'
$bas = Get-Date
$p = Start-Process -FilePath 'dotnet' -ArgumentList "`"$Bin\Macria.dll`"" -WorkingDirectory $Bin -PassThru -WindowStyle Hidden
$env:MACRIA_OTOMASYON = ''; $env:MACRIA_OTOMASYON_KAPAN = ''
$enCok = 0
while (-not $p.HasExited -and ((Get-Date) - $bas).TotalSeconds -lt 40) {
    $ekranda = 0
    $cb = [W3.U+EnumProc] {
        param($h, $l)
        [uint32]$sahip = 0; [void][W3.U]::GetWindowThreadProcessId($h, [ref]$sahip)
        if ($sahip -eq $p.Id -and [W3.U]::IsWindowVisible($h)) {
            $r = New-Object W3.U+RECT; [void][W3.U]::GetWindowRect($h, [ref]$r)
            if ($r.R -gt 0 -and $r.B -gt 0 -and ($r.R - $r.L) -gt 1) { $script:ekranda++ }
        }
        return $true
    }
    [void][W3.U]::EnumWindows($cb, [IntPtr]::Zero)
    if ($ekranda -gt $enCok) { $enCok = $ekranda }
    Start-Sleep -Milliseconds 300
    $p.Refresh()
}
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; "KAPANMADI, zorla durduruldu" }
"ekranda gorunen en fazla pencere: $enCok; kapandi: $($p.HasExited), cikis 0x{0:X8}, sure {1:0} s" -f $p.ExitCode, ((Get-Date) - $bas).TotalSeconds
