# Generates the intro narration with Azure Text-to-Speech (voice: YunJhe, pitch -15%).
# The lines are read straight from IntroSequence.cs so the text never drifts.
#
# Needs two User environment variables (never commit the key):
#   AZURE_SPEECH_KEY     key of an Azure "Speech" resource (free tier F0 is enough)
#   AZURE_SPEECH_REGION  e.g. eastasia
#
# Usage (from repo root):  pwsh tools/tts/gen_intro_voice.ps1
# Optional: -Voice zh-TW-YunJheNeural -Pitch "-15%" -Rate "0%"
param(
    [string]$Voice = 'zh-TW-YunJheNeural',
    [string]$Pitch = '-15%',
    [string]$Rate = '0%'
)
$ErrorActionPreference = 'Stop'

$key = $env:AZURE_SPEECH_KEY; if (-not $key) { $key = [Environment]::GetEnvironmentVariable('AZURE_SPEECH_KEY', 'User') }
$region = $env:AZURE_SPEECH_REGION; if (-not $region) { $region = [Environment]::GetEnvironmentVariable('AZURE_SPEECH_REGION', 'User') }
if (-not $key -or -not $region) { throw 'Set AZURE_SPEECH_KEY and AZURE_SPEECH_REGION (User environment variables) first.' }

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$src = [IO.File]::ReadAllText((Join-Path $root 'client/Assets/Scripts/UI/IntroSequence.cs'))
$block = [regex]::Match($src, 'string\[\] Lines =\s*\{(.*?)\};', 'Singleline').Groups[1].Value
$lines = [regex]::Matches($block, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
if ($lines.Count -eq 0) { throw 'Could not read Lines from IntroSequence.cs' }

$out = Join-Path $root 'client/Assets/Resources/Audio/Intro'
New-Item -ItemType Directory -Force $out | Out-Null
$uri = "https://$region.tts.speech.microsoft.com/cognitiveservices/v1"
$headers = @{
    'Ocp-Apim-Subscription-Key' = $key
    'X-Microsoft-OutputFormat'  = 'riff-24khz-16bit-mono-pcm'
    'User-Agent'                = 'AbyssKingdomTools'
}

for ($i = 0; $i -lt $lines.Count; $i++) {
    $text = [Security.SecurityElement]::Escape($lines[$i])
    $ssml = @"
<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='zh-TW'>
  <voice name='$Voice'><prosody pitch='$Pitch' rate='$Rate'>$text</prosody></voice>
</speak>
"@
    $file = Join-Path $out ("line{0}.wav" -f ($i + 1))
    Invoke-WebRequest -Uri $uri -Method Post -Headers $headers -ContentType 'application/ssml+xml; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes($ssml)) -OutFile $file
    $bytes = (Get-Item $file).Length
    '{0}  {1:N1} s' -f (Split-Path -Leaf $file), (($bytes - 44) / 48000.0)
}
