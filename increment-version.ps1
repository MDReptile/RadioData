# Auto-increment version script for RadioData
# Run automatically on git push

$projectFile = "RadioDataApp\RadioDataApp.csproj"

# Read current version
[xml]$project = Get-Content $projectFile
$currentVersion = $project.Project.PropertyGroup.Version

Write-Host "Current version: $currentVersion" -ForegroundColor Cyan

# Parse version (format: X.YY)
$parts = $currentVersion.Split('.')
$major = [int]$parts[0]
$minor = [int]$parts[1]

# Increment minor version
$minor++

# Check for rollover to next major version
if ($minor -ge 100) {
    $major++
    $minor = 0
}

# Format new version
$newVersion = "{0}.{1:D2}" -f $major, $minor
$assemblyVersion = "{0}.{1}.0.0" -f $major, $minor

Write-Host "New version: $newVersion" -ForegroundColor Green

# Update project file
$project.Project.PropertyGroup.Version = $newVersion
$project.Project.PropertyGroup.AssemblyVersion = $assemblyVersion
$project.Project.PropertyGroup.FileVersion = $assemblyVersion
$project.Save($projectFile)

Write-Host "Version updated successfully!" -ForegroundColor Green
