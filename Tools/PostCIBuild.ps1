$bf = $Env:APPVEYOR_BUILD_FOLDER

$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo("$bf/IconDiffBot.Tests/bin/Release/IconDiffBot.Tests.dll").FileVersion
