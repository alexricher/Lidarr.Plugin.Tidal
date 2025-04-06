# Add Missing Documentation Script
# This script helps add missing XML documentation to C# files
# Usage: .\AddMissingDocumentation.ps1 -File "path/to/file.cs"

param(
    [Parameter(Mandatory=$false)]
    [string]$File,
    
    [Parameter(Mandatory=$false)]
    [switch]$ListUndocumented,
    
    [Parameter(Mandatory=$false)]
    [switch]$GenerateTemplates
)

$ErrorActionPreference = "Stop"

# Function to create documentation template for a class
function Create-ClassDocumentation {
    param (
        [string]$className,
        [string]$fileContent
    )
    
    # Determine if this is an interface or a class
    $isInterface = $fileContent -match "public\s+interface\s+$className"
    $isEnum = $fileContent -match "public\s+enum\s+$className"
    
    if ($isInterface) {
        @"
/// <summary>
/// Defines functionality for [brief description of the interface's purpose].
/// </summary>
"@
    } 
    elseif ($isEnum) {
        @"
/// <summary>
/// Enumerates [brief description of what the enum represents].
/// </summary>
"@
    }
    else {
        @"
/// <summary>
/// Provides functionality for [brief description of the class's purpose].
/// </summary>
/// <remarks>
/// [Additional information about using the class, implementation details, etc.]
/// </remarks>
"@
    }
}

# Function to create documentation template for a method
function Create-MethodDocumentation {
    param (
        [string]$methodName,
        [string]$methodSignature
    )
    
    # Extract parameters
    $paramMatches = [regex]::Matches($methodSignature, '\s*(\w+(?:<[^>]+>)?(?:\[\])?)\s+(\w+)(?:\s*=\s*[^,)]+)?')
    $returnType = [regex]::Match($methodSignature, '^\s*(?:public|protected|private|internal|static)?\s+([^\s]+)')
    
    $docs = @"
/// <summary>
/// [Brief description of what the method does, starting with a verb].
/// </summary>

"@
    
    # Add parameter documentation
    foreach ($param in $paramMatches) {
        $paramName = $param.Groups[2].Value
        $docs += "/// <param name=`"$paramName`">[Description of the parameter].</param>`n"
    }
    
    # Add return documentation if not void
    if ($returnType.Groups[1].Value -ne "void") {
        $docs += "/// <returns>[Description of the return value].</returns>`n"
    }
    
    $docs += "/// <exception cref=`"Exception`">[Conditions under which exception is thrown].</exception>`n"
    $docs += "/// <remarks>[Optional additional information].</remarks>"
    
    return $docs
}

# Function to create documentation template for a property
function Create-PropertyDocumentation {
    param (
        [string]$propertyName,
        [string]$propertySignature
    )
    
    # Determine if it's a getter, setter, or both
    $isGetter = $propertySignature -match "get\s*;"
    $isSetter = $propertySignature -match "set\s*;"
    
    $accessDescription = if ($isGetter -and $isSetter) {
        "Gets or sets"
    } elseif ($isGetter) {
        "Gets"
    } elseif ($isSetter) {
        "Sets"
    } else {
        "Represents"
    }
    
    @"
/// <summary>
/// $accessDescription [description of what the property represents].
/// </summary>
/// <value>[Description of the property's value].</value>
"@
}

# Function to find undocumented members
function Find-UndocumentedMembers {
    param (
        [string]$filePath
    )
    
    if (-not (Test-Path $filePath)) {
        Write-Error "File not found: $filePath"
        return
    }
    
    $fileContent = Get-Content $filePath -Raw
    $fileName = Split-Path $filePath -Leaf
    $undocumented = @()
    
    # Check for public classes without documentation
    $classMatches = [regex]::Matches($fileContent, '(?<!\/\/\/\s*<summary>[\s\S]*?)public\s+(class|interface|enum|struct)\s+(\w+)')
    foreach ($match in $classMatches) {
        $undocumented += @{
            Type = "Class/Interface"
            Name = $match.Groups[2].Value
            LineNumber = ($fileContent.Substring(0, $match.Index).Split("`n")).Length
            Signature = $match.Value
        }
    }
    
    # Check for public methods without documentation
    $methodMatches = [regex]::Matches($fileContent, '(?<!\/\/\/\s*<summary>[\s\S]*?)public\s+[\w<>\[\]]+\s+(\w+)\s*\(([^\)]*)\)')
    foreach ($match in $methodMatches) {
        # Skip auto-generated methods
        if ($match.Value -match "\[.*generated.*\]" -or $match.Value -match "=>") {
            continue
        }
        
        $undocumented += @{
            Type = "Method"
            Name = $match.Groups[1].Value
            LineNumber = ($fileContent.Substring(0, $match.Index).Split("`n")).Length
            Signature = $match.Value
        }
    }
    
    # Check for public properties without documentation
    $propMatches = [regex]::Matches($fileContent, '(?<!\/\/\/\s*<summary>[\s\S]*?)public\s+([\w<>\[\]]+)\s+(\w+)\s*\{([^}]*)\}')
    foreach ($match in $propMatches) {
        # Skip auto-properties with attributes
        if ($match.Value -match "\[.*\]") {
            continue
        }
        
        $undocumented += @{
            Type = "Property"
            Name = $match.Groups[2].Value
            LineNumber = ($fileContent.Substring(0, $match.Index).Split("`n")).Length
            Signature = $match.Value
        }
    }
    
    return $undocumented
}

# Function to generate documentation templates
function Generate-DocumentationTemplates {
    param (
        [string]$filePath
    )
    
    if (-not (Test-Path $filePath)) {
        Write-Error "File not found: $filePath"
        return
    }
    
    $undocumented = Find-UndocumentedMembers -filePath $filePath
    $fileContent = Get-Content $filePath -Raw
    $fileLines = Get-Content $filePath
    $templates = @{}
    $fileName = Split-Path $filePath -Leaf
    
    foreach ($member in $undocumented) {
        $line = $member.LineNumber - 1  # 0-based index
        
        $template = ""
        switch ($member.Type) {
            "Class/Interface" {
                $template = Create-ClassDocumentation -className $member.Name -fileContent $fileContent
            }
            "Method" {
                $template = Create-MethodDocumentation -methodName $member.Name -methodSignature $member.Signature
            }
            "Property" {
                $template = Create-PropertyDocumentation -propertyName $member.Name -propertySignature $member.Signature
            }
        }
        
        $templates[$line] = $template
    }
    
    # Determine the newline style used in the file
    $newline = if ($fileContent -match "`r`n") { "`r`n" } else { "`n" }
    
    # Generate a new file with documentation templates inserted
    $newFileLines = @()
    for ($i = 0; $i -lt $fileLines.Count; $i++) {
        if ($templates.ContainsKey($i)) {
            $newFileLines += $templates[$i].Split("`n")
        }
        $newFileLines += $fileLines[$i]
    }
    
    $outputPath = [System.IO.Path]::Combine([System.IO.Path]::GetDirectoryName($filePath), 
                                             [System.IO.Path]::GetFileNameWithoutExtension($filePath) + "_documented.cs")
    
    $newFileLines -join $newline | Set-Content -Path $outputPath -NoNewline
    
    Write-Host "Documentation templates generated. New file saved as: $outputPath" -ForegroundColor Green
    return $outputPath
}

function Display-UndocumentedMembers {
    param (
        [string]$filePath
    )
    
    $undocumented = Find-UndocumentedMembers -filePath $filePath
    $fileName = Split-Path $filePath -Leaf
    
    if ($undocumented.Count -eq 0) {
        Write-Host "All members in '$fileName' are documented. Good job!" -ForegroundColor Green
        return
    }
    
    Write-Host "Undocumented members in '$fileName':" -ForegroundColor Yellow
    
    $undocumented | ForEach-Object {
        Write-Host "Line $($_.LineNumber): $($_.Type) '$($_.Name)'" -ForegroundColor Cyan
    }
    
    Write-Host "`nUse -GenerateTemplates switch to generate documentation templates." -ForegroundColor Yellow
}

# Main script logic
if (-not $File -and -not $ListUndocumented) {
    # No file specified, scan the entire src directory for undocumented members
    $projectRoot = Join-Path $PSScriptRoot ".."
    $srcFolder = Join-Path $projectRoot "src"
    
    if (-not (Test-Path $srcFolder)) {
        Write-Error "Source folder not found: $srcFolder"
        exit 1
    }
    
    $csFiles = Get-ChildItem -Path $srcFolder -Filter "*.cs" -Recurse
    $filesWithUndocumentedMembers = @()
    
    foreach ($csFile in $csFiles) {
        $undocumented = Find-UndocumentedMembers -filePath $csFile.FullName
        if ($undocumented.Count -gt 0) {
            $filesWithUndocumentedMembers += @{
                File = $csFile.FullName
                Count = $undocumented.Count
            }
        }
    }
    
    if ($filesWithUndocumentedMembers.Count -eq 0) {
        Write-Host "All members in all files are documented. Good job!" -ForegroundColor Green
        exit 0
    }
    
    Write-Host "Files with undocumented members:" -ForegroundColor Yellow
    $filesWithUndocumentedMembers | ForEach-Object {
        Write-Host "$($_.File): $($_.Count) undocumented members" -ForegroundColor Cyan
    }
    
    Write-Host "`nTo view undocumented members in a specific file, use: AddMissingDocumentation.ps1 -File <path> -ListUndocumented" -ForegroundColor Yellow
    Write-Host "To generate documentation templates for a file, use: AddMissingDocumentation.ps1 -File <path> -GenerateTemplates" -ForegroundColor Yellow
    
    exit 0
}

if ($File) {
    if ($ListUndocumented) {
        Display-UndocumentedMembers -filePath $File
    }
    elseif ($GenerateTemplates) {
        Generate-DocumentationTemplates -filePath $File
    }
    else {
        # Default behavior with just a file: list undocumented members
        Display-UndocumentedMembers -filePath $File
    }
}
else {
    Write-Host "Please specify a file path using -File parameter." -ForegroundColor Yellow
} 