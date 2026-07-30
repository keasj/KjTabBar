param(
    [Parameter(Mandatory = $true)]
    [string[]]$MsiPath
)

$ErrorActionPreference = 'Stop'

$actionName = 'KjTabBarCloseRunningProcess'
$scriptBinaryName = 'KjTabBarCloseRunningProcessScript'
$previousActionBinaryName = 'KjTabBarCloseRunningProcessBinary'
$actionType = 6
$actionTarget = 'CloseRunningKjTabBar'
$scriptContent = @"
Function GetRunningKjTabBarCount(service)
    On Error Resume Next
    Dim processes

    Err.Clear
    Set processes = service.ExecQuery("SELECT ProcessId FROM Win32_Process WHERE Name='KjTabBar.exe'")
    If Err.Number <> 0 Then
        GetRunningKjTabBarCount = -1
        Exit Function
    End If

    Err.Clear
    GetRunningKjTabBarCount = processes.Count
    If Err.Number <> 0 Then
        GetRunningKjTabBarCount = -1
    End If
End Function

Function WaitForKjTabBarExit(service)
    On Error Resume Next
    Dim attempt
    Dim deletionEvent
    Dim eventSource
    Dim runningCount

    runningCount = GetRunningKjTabBarCount(service)
    If runningCount = 0 Then
        WaitForKjTabBarExit = True
        Exit Function
    End If
    If runningCount < 0 Then
        WaitForKjTabBarExit = False
        Exit Function
    End If

    Err.Clear
    Set eventSource = service.ExecNotificationQuery("SELECT * FROM __InstanceDeletionEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process' AND TargetInstance.Name='KjTabBar.exe'")
    If Err.Number <> 0 Then
        WaitForKjTabBarExit = False
        Exit Function
    End If

    For attempt = 1 To 10
        Err.Clear
        Set deletionEvent = eventSource.NextEvent(1000)
        Err.Clear

        runningCount = GetRunningKjTabBarCount(service)
        If runningCount = 0 Then
            WaitForKjTabBarExit = True
            Exit Function
        End If
        If runningCount < 0 Then
            WaitForKjTabBarExit = False
            Exit Function
        End If
    Next

    WaitForKjTabBarExit = False
End Function

Function CloseRunningKjTabBar()
    On Error Resume Next
    Dim process
    Dim processes
    Dim runningCount
    Dim service

    CloseRunningKjTabBar = 3
    Err.Clear
    Set service = GetObject("winmgmts:{impersonationLevel=impersonate}!\\.\root\cimv2")
    If Err.Number <> 0 Then
        Exit Function
    End If

    runningCount = GetRunningKjTabBarCount(service)
    If runningCount < 0 Then
        Exit Function
    End If
    If runningCount = 0 Then
        CloseRunningKjTabBar = 1
        Exit Function
    End If

    Err.Clear
    Set processes = service.ExecQuery("SELECT * FROM Win32_Process WHERE Name='KjTabBar.exe'")
    If Err.Number <> 0 Then
        Exit Function
    End If

    For Each process In processes
        Err.Clear
        process.Terminate()
        Err.Clear
    Next

    If WaitForKjTabBarExit(service) Then
        CloseRunningKjTabBar = 1
    End If
End Function
"@

function ConvertTo-MsiSqlLiteral {
    param(
        [string]$Value
    )

    if ($null -eq $Value) {
        return ''
    }

    return $Value.Replace("'", "''")
}

function Invoke-MsiSql {
    param(
        [object]$Database,
        [string]$Sql
    )

    $view = $Database.GetType().InvokeMember(
        'OpenView',
        'InvokeMethod',
        $null,
        $Database,
        @($Sql))
    try {
        $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    }
    finally {
        if ($null -ne $view) {
            $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($view) | Out-Null
        }
    }
}

function Get-MsiScalar {
    param(
        [object]$Database,
        [string]$Sql
    )

    $view = $Database.GetType().InvokeMember(
        'OpenView',
        'InvokeMethod',
        $null,
        $Database,
        @($Sql))
    try {
        $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
        $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $record) {
            return $null
        }

        try {
            return $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(1))
        }
        finally {
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($record) | Out-Null
        }
    }
    finally {
        if ($null -ne $view) {
            $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($view) | Out-Null
        }
    }
}

function Set-MsiBinaryTextStream {
    param(
        [object]$Installer,
        [object]$Database,
        [string]$Name,
        [string]$Text
    )

    $escapedName = ConvertTo-MsiSqlLiteral $Name
    $tempScriptPath = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), $Name + '_' + [System.Guid]::NewGuid().ToString('N') + '.vbs')
    [System.IO.File]::WriteAllText($tempScriptPath, $Text, [System.Text.Encoding]::ASCII)

    $view = $null
    $record = $null
    try {
        Invoke-MsiSql $Database "DELETE FROM ``Binary`` WHERE ``Name``='$escapedName'"
        $view = $Database.GetType().InvokeMember(
            'OpenView',
            'InvokeMethod',
            $null,
            $Database,
            @("INSERT INTO ``Binary`` (``Name``, ``Data``) VALUES ('$escapedName', ?)"))
        $record = $Installer.GetType().InvokeMember('CreateRecord', 'InvokeMethod', $null, $Installer, @(1))
        $record.GetType().InvokeMember('SetStream', 'InvokeMethod', $null, $record, @(1, $tempScriptPath)) | Out-Null
        $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, @($record)) | Out-Null
    }
    finally {
        if ($null -ne $record) {
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($record) | Out-Null
        }

        if ($null -ne $view) {
            $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($view) | Out-Null
        }

        if ([System.IO.File]::Exists($tempScriptPath)) {
            [System.IO.File]::Delete($tempScriptPath)
        }
    }
}

function Add-PreValidateSequenceAction {
    param(
        [object]$Database,
        [string]$TableName,
        [string]$ActionName,
        [bool]$Required
    )

    $escapedActionName = ConvertTo-MsiSqlLiteral $ActionName
    Invoke-MsiSql $Database "DELETE FROM ``$TableName`` WHERE ``Action``='$escapedActionName'"

    $installValidateSequence = Get-MsiScalar $Database "SELECT ``Sequence`` FROM ``$TableName`` WHERE ``Action``='InstallValidate'"
    if ([string]::IsNullOrEmpty($installValidateSequence)) {
        if ($Required) {
            throw "$TableName does not contain InstallValidate. Cannot add $ActionName."
        }

        Write-Host "$TableName does not contain InstallValidate. Skipping $ActionName."
        return
    }

    [int]$sequence = [int]$installValidateSequence - 1
    if ($sequence -lt 1) {
        $sequence = 1
    }

    Invoke-MsiSql $Database "INSERT INTO ``$TableName`` (``Action``, ``Condition``, ``Sequence``) VALUES ('$escapedActionName', '', $sequence)"
}

function Confirm-PreValidateSequenceAction {
    param(
        [object]$Database,
        [string]$ActionName
    )

    $escapedActionName = ConvertTo-MsiSqlLiteral $ActionName
    $actionSequence = Get-MsiScalar $Database "SELECT ``Sequence`` FROM ``InstallExecuteSequence`` WHERE ``Action``='$escapedActionName'"
    $installValidateSequence = Get-MsiScalar $Database "SELECT ``Sequence`` FROM ``InstallExecuteSequence`` WHERE ``Action``='InstallValidate'"

    if ([string]::IsNullOrEmpty($actionSequence) -or [string]::IsNullOrEmpty($installValidateSequence)) {
        throw "$ActionName was not added to InstallExecuteSequence."
    }

    if ([int]$actionSequence -ge [int]$installValidateSequence) {
        throw "$ActionName must run before InstallValidate. Current sequence: $actionSequence, InstallValidate: $installValidateSequence."
    }

    Write-Host "$ActionName scheduled before InstallValidate: $actionSequence < $installValidateSequence"
}

function Confirm-CloseProcessCustomAction {
    param(
        [object]$Database,
        [string]$ActionName,
        [int]$ExpectedType,
        [string]$ExpectedBinaryName,
        [string]$ExpectedTarget
    )

    $escapedActionName = ConvertTo-MsiSqlLiteral $ActionName
    $escapedBinaryName = ConvertTo-MsiSqlLiteral $ExpectedBinaryName
    $actualType = Get-MsiScalar $Database "SELECT ``Type`` FROM ``CustomAction`` WHERE ``Action``='$escapedActionName'"
    $actualSource = Get-MsiScalar $Database "SELECT ``Source`` FROM ``CustomAction`` WHERE ``Action``='$escapedActionName'"
    $actualTarget = Get-MsiScalar $Database "SELECT ``Target`` FROM ``CustomAction`` WHERE ``Action``='$escapedActionName'"
    $binaryName = Get-MsiScalar $Database "SELECT ``Name`` FROM ``Binary`` WHERE ``Name``='$escapedBinaryName'"

    if ([string]::IsNullOrEmpty($binaryName)) {
        throw "$ExpectedBinaryName was not added to the Binary table."
    }

    if ([int]$actualType -ne $ExpectedType -or $actualSource -ne $ExpectedBinaryName -or $actualTarget -ne $ExpectedTarget) {
        throw "$ActionName CustomAction row is not the expected in-process VBScript action."
    }
}

function Set-RegularShortcutPolicy {
    param(
        [object]$Database
    )

    $propertyValue = Get-MsiScalar $Database "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='DISABLEADVTSHORTCUTS'"
    if ([string]::IsNullOrEmpty($propertyValue)) {
        Invoke-MsiSql $Database "INSERT INTO ``Property`` (``Property``, ``Value``) VALUES ('DISABLEADVTSHORTCUTS', '1')"
    }
    else {
        Invoke-MsiSql $Database "UPDATE ``Property`` SET ``Value``='1' WHERE ``Property``='DISABLEADVTSHORTCUTS'"
    }

    Invoke-MsiSql $Database "UPDATE ``Shortcut`` SET ``Icon_``=NULL, ``IconIndex``=NULL"
}

function Confirm-RegularShortcutPolicy {
    param(
        [object]$Database
    )

    $propertyValue = Get-MsiScalar $Database "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='DISABLEADVTSHORTCUTS'"
    $shortcutIcon = Get-MsiScalar $Database "SELECT ``Icon_`` FROM ``Shortcut`` WHERE ``Icon_`` IS NOT NULL"
    $shortcutIconIndex = Get-MsiScalar $Database "SELECT ``IconIndex`` FROM ``Shortcut`` WHERE ``IconIndex`` IS NOT NULL"

    if ($propertyValue -ne '1') {
        throw 'DISABLEADVTSHORTCUTS was not set to 1.'
    }

    if (-not [string]::IsNullOrEmpty($shortcutIcon) -or -not [string]::IsNullOrEmpty($shortcutIconIndex)) {
        throw 'Shortcut icon overrides were not removed.'
    }
}

function Set-VersionIndependentUpgradePolicy {
    param(
        [object]$Database
    )

    $upgradeCode = Get-MsiScalar $Database "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='UpgradeCode'"
    if ([string]::IsNullOrEmpty($upgradeCode)) {
        throw 'UpgradeCode is missing from the Property table.'
    }

    $escapedUpgradeCode = ConvertTo-MsiSqlLiteral $upgradeCode
    Invoke-MsiSql $Database "DELETE FROM ``Upgrade`` WHERE ``UpgradeCode``='$escapedUpgradeCode'"
    Invoke-MsiSql $Database "INSERT INTO ``Upgrade`` (``UpgradeCode``, ``VersionMin``, ``VersionMax``, ``Language``, ``Attributes``, ``Remove``, ``ActionProperty``) VALUES ('$escapedUpgradeCode', '0.0.0', NULL, NULL, 256, NULL, 'PREVIOUSVERSIONSINSTALLED')"
}

function Confirm-VersionIndependentUpgradePolicy {
    param(
        [object]$Database
    )

    $upgradeCode = Get-MsiScalar $Database "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='UpgradeCode'"
    $escapedUpgradeCode = ConvertTo-MsiSqlLiteral $upgradeCode
    $versionMin = Get-MsiScalar $Database "SELECT ``VersionMin`` FROM ``Upgrade`` WHERE ``UpgradeCode``='$escapedUpgradeCode' AND ``ActionProperty``='PREVIOUSVERSIONSINSTALLED'"
    $versionMax = Get-MsiScalar $Database "SELECT ``VersionMax`` FROM ``Upgrade`` WHERE ``UpgradeCode``='$escapedUpgradeCode' AND ``ActionProperty``='PREVIOUSVERSIONSINSTALLED'"
    $attributes = Get-MsiScalar $Database "SELECT ``Attributes`` FROM ``Upgrade`` WHERE ``UpgradeCode``='$escapedUpgradeCode' AND ``ActionProperty``='PREVIOUSVERSIONSINSTALLED'"
    $newerProductAction = Get-MsiScalar $Database "SELECT ``ActionProperty`` FROM ``Upgrade`` WHERE ``UpgradeCode``='$escapedUpgradeCode' AND ``ActionProperty``='NEWERPRODUCTFOUND'"

    if ($versionMin -ne '0.0.0' -or -not [string]::IsNullOrEmpty($versionMax) -or [int]$attributes -ne 256) {
        throw 'The version-independent Upgrade row was not configured as expected.'
    }

    if (-not [string]::IsNullOrEmpty($newerProductAction)) {
        throw 'The newer-product blocking Upgrade row was not removed.'
    }

    Write-Host 'Upgrade policy replaces any installed KjTabBar version without requiring a manual uninstall.'
}

foreach ($path in $MsiPath) {
    $resolvedPath = (Resolve-Path $path).Path
    Write-Host "Patching MSI pre-close action: $resolvedPath"

    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.GetType().InvokeMember(
        'OpenDatabase',
        'InvokeMethod',
        $null,
        $installer,
        @($resolvedPath, 1))

    try {
        $escapedActionName = ConvertTo-MsiSqlLiteral $actionName
        $escapedPreviousActionBinaryName = ConvertTo-MsiSqlLiteral $previousActionBinaryName
        $escapedScriptBinaryName = ConvertTo-MsiSqlLiteral $scriptBinaryName
        $escapedActionTarget = ConvertTo-MsiSqlLiteral $actionTarget

        Invoke-MsiSql $database "DELETE FROM ``CustomAction`` WHERE ``Action``='$escapedActionName'"
        Invoke-MsiSql $database "DELETE FROM ``Binary`` WHERE ``Name``='$escapedPreviousActionBinaryName'"
        Set-MsiBinaryTextStream $installer $database $scriptBinaryName $scriptContent
        Invoke-MsiSql $database "INSERT INTO ``CustomAction`` (``Action``, ``Type``, ``Source``, ``Target``) VALUES ('$escapedActionName', $actionType, '$escapedScriptBinaryName', '$escapedActionTarget')"

        Add-PreValidateSequenceAction $database 'InstallUISequence' $actionName $false
        Add-PreValidateSequenceAction $database 'InstallExecuteSequence' $actionName $true
        Confirm-CloseProcessCustomAction $database $actionName $actionType $scriptBinaryName $actionTarget
        Confirm-PreValidateSequenceAction $database $actionName
        Set-RegularShortcutPolicy $database
        Confirm-RegularShortcutPolicy $database
        Set-VersionIndependentUpgradePolicy $database
        Confirm-VersionIndependentUpgradePolicy $database

        $database.GetType().InvokeMember('Commit', 'InvokeMethod', $null, $database, $null) | Out-Null
    }
    finally {
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($database) | Out-Null
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($installer) | Out-Null
    }
}