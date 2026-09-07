# Define the character set (letters, numbers, and common symbols)
$Characters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_-'

# Define the desired length of the key
$KeyLength = 32

# Initialize an empty array to hold the random characters
$RandomKeyChars = @()

Write-Host "Generating a secure API Key of $KeyLength characters..."

# Loop until the required number of characters is generated
while ($RandomKeyChars.Count -lt $KeyLength) {
    # Get a random index from the character set
    $RandomIndex = Get-Random -Minimum 0 -Maximum $($Characters.Length - 1)
    
    # Add the randomly selected character to the array
    $RandomKeyChars += $Characters[$RandomIndex]
}

# Join the characters into a single string and display it
$GeneratedKey = $RandomKeyChars -join ""

Write-Host "=============================================="
Write-Host "✅ Generated API Key: $GeneratedKey"
Write-Host "=============================================="

# Optional: You can save this key to a file for safekeeping
# Out-File -FilePath ".\api_key.txt" -InputObject $GeneratedKey
