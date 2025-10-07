# Test script for file upload
$uri = "http://localhost:5289/api/files/upload"
$filePath = "C:\dev\code\silo\testfile.txt"

# Create form data
$boundary = [System.Guid]::NewGuid().ToString()
$LF = "`r`n"
$fileBytes = [System.IO.File]::ReadAllBytes($filePath)
$fileName = [System.IO.Path]::GetFileName($filePath)

$bodyLines = (
    "--$boundary",
    "Content-Disposition: form-data; name=`"file`"; filename=`"$fileName`"",
    "Content-Type: text/plain$LF",
    [System.Text.Encoding]::UTF8.GetString($fileBytes),
    "--$boundary--$LF"
) -join $LF

$body = [System.Text.Encoding]::UTF8.GetBytes($bodyLines)

try {
    $response = Invoke-RestMethod -Uri $uri -Method Post -Body $body -ContentType "multipart/form-data; boundary=$boundary"
    Write-Host "SUCCESS - File uploaded successfully!" -ForegroundColor Green
    Write-Host "Response:" -ForegroundColor Yellow
    $response | ConvertTo-Json -Depth 10
    
    # Return the FileId for further testing
    return $response.FileId
} catch {
    Write-Host "ERROR - File upload failed!" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    return $null
}