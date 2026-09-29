[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourcePath,
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][int]$WaitForProcess
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$form = New-Object System.Windows.Forms.Form
$form.Text = 'EntityTracker update'
$form.Size = New-Object System.Drawing.Size(640, 390)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false

$heading = New-Object System.Windows.Forms.Label
$heading.Text = "Building $Tag on this computer"
$heading.Font = New-Object System.Drawing.Font('Segoe UI', 13,
    [System.Drawing.FontStyle]::Bold)
$heading.Location = New-Object System.Drawing.Point(20, 18)
$heading.Size = New-Object System.Drawing.Size(590, 32)
$form.Controls.Add($heading)

$description = New-Object System.Windows.Forms.Label
$description.Text = 'EntityTracker will reopen when the update is ready.'
$description.Location = New-Object System.Drawing.Point(20, 56)
$description.Size = New-Object System.Drawing.Size(590, 24)
$form.Controls.Add($description)

$progress = New-Object System.Windows.Forms.ProgressBar
$progress.Style = 'Marquee'
$progress.Location = New-Object System.Drawing.Point(20, 91)
$progress.Size = New-Object System.Drawing.Size(584, 12)
$form.Controls.Add($progress)

$log = New-Object System.Windows.Forms.TextBox
$log.Multiline = $true
$log.AcceptsReturn = $true
$log.ReadOnly = $true
$log.ScrollBars = 'Vertical'
$log.Location = New-Object System.Drawing.Point(20, 120)
$log.Size = New-Object System.Drawing.Size(584, 180)
$form.Controls.Add($log)

$retry = New-Object System.Windows.Forms.Button
$retry.Text = 'Retry update'
$retry.Location = New-Object System.Drawing.Point(402, 314)
$retry.Size = New-Object System.Drawing.Size(100, 30)
$retry.Enabled = $false
$form.Controls.Add($retry)

$exit = New-Object System.Windows.Forms.Button
$exit.Text = 'Exit'
$exit.Location = New-Object System.Drawing.Point(510, 314)
$exit.Size = New-Object System.Drawing.Size(94, 30)
$exit.Add_Click({ $form.Close() })
$form.Controls.Add($exit)

$successPage = New-Object System.Windows.Forms.Panel
$successPage.Location = New-Object System.Drawing.Point(0, 0)
$successPage.Size = $form.ClientSize
$successPage.Visible = $false
$form.Controls.Add($successPage)

$successHeading = New-Object System.Windows.Forms.Label
$successHeading.Text = 'Update complete'
$successHeading.Font = New-Object System.Drawing.Font('Segoe UI', 13,
    [System.Drawing.FontStyle]::Bold)
$successHeading.Location = New-Object System.Drawing.Point(20, 18)
$successHeading.Size = New-Object System.Drawing.Size(590, 32)
$successPage.Controls.Add($successHeading)

$successDescription = New-Object System.Windows.Forms.Label
$successDescription.Text = "EntityTracker was updated to $Tag and is ready to use."
$successDescription.Location = New-Object System.Drawing.Point(20, 56)
$successDescription.Size = New-Object System.Drawing.Size(590, 48)
$successPage.Controls.Add($successDescription)

$finish = New-Object System.Windows.Forms.Button
$finish.Text = 'Finish'
$finish.Location = New-Object System.Drawing.Point(510, 314)
$finish.Size = New-Object System.Drawing.Size(94, 30)
$finish.Add_Click({ $form.Close() })
$successPage.Controls.Add($finish)

$script:worker = $null
$script:stdoutPath = $null
$script:stderrPath = $null
$script:attempt = 0

function Start-UpdateAttempt {
    $script:attempt++
    $retry.Enabled = $false
    $progress.Style = 'Marquee'
    $heading.Text = "Building $Tag on this computer"
    $log.Text = 'Preparing update...'
    $script:stdoutPath = Join-Path $PSScriptRoot "update-$($script:attempt).out.log"
    $script:stderrPath = Join-Path $PSScriptRoot "update-$($script:attempt).err.log"
    try {
        $script:worker = Start-Process -FilePath 'powershell.exe' -PassThru -WindowStyle Hidden `
            -WorkingDirectory $PSScriptRoot `
            -ArgumentList @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File',
                ('"' + (Join-Path $PSScriptRoot 'Install-EntityTracker.ps1') + '"'),
                '-SourcePath', ('"' + $SourcePath + '"'), '-Tag', $Tag,
                '-WaitForProcess', $WaitForProcess) `
            -RedirectStandardOutput $script:stdoutPath -RedirectStandardError $script:stderrPath
    }
    catch {
        $progress.Style = 'Blocks'
        $heading.Text = 'Update failed'
        $log.Text = $_.Exception.Message
        $retry.Enabled = $true
    }
}

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 400
$timer.Add_Tick({
    if (-not $script:worker) { return }
    $output = if (Test-Path -LiteralPath $script:stdoutPath) {
        Get-Content -LiteralPath $script:stdoutPath -Raw -ErrorAction SilentlyContinue
    } else { '' }
    $errors = if (Test-Path -LiteralPath $script:stderrPath) {
        Get-Content -LiteralPath $script:stderrPath -Raw -ErrorAction SilentlyContinue
    } else { '' }
    $log.Text = ($output + [Environment]::NewLine + $errors).TrimEnd()
    $log.SelectionStart = $log.TextLength
    $log.ScrollToCaret()
    if ($script:worker.HasExited) {
        $code = $script:worker.ExitCode
        $script:worker.Dispose()
        $script:worker = $null
        if ($code -eq 0) {
            $timer.Stop()
            $heading.Visible = $false
            $description.Visible = $false
            $progress.Visible = $false
            $log.Visible = $false
            $retry.Visible = $false
            $exit.Visible = $false
            $successPage.Visible = $true
            $successPage.BringToFront()
            $form.AcceptButton = $finish
        } else {
            $progress.Style = 'Blocks'
            $heading.Text = 'Update failed'
            if (-not $log.Text) { $log.Text = "Build failed with exit code $code." }
            $retry.Enabled = $true
        }
    }
})

$retry.Add_Click({ Start-UpdateAttempt })
$form.Add_Shown({ Start-UpdateAttempt; $timer.Start() })
[void][System.Windows.Forms.Application]::Run($form)
$timer.Dispose()
$form.Dispose()
