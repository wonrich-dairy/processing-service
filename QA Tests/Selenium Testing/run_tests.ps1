# Wonrich Dairy Selenium - Run tests against Vercel live - PowerShell
Write-Host "Wonrich Dairy Selenium Tests - Vercel Live" -ForegroundColor Green
Write-Host "URL: https://frontend-phi-sage-81.vercel.app/" -ForegroundColor Cyan
Write-Host ""

python --version

Write-Host "Installing dependencies..." -ForegroundColor Yellow
pip install -r requirements.txt

if (-not (Test-Path .env)) {
  Write-Host "WARNING: .env not found, copying from .env.example" -ForegroundColor Red
  Copy-Item .env.example .env
  Write-Host "Please edit .env with real Employee ID and PIN" -ForegroundColor Red
}

Write-Host ""
Write-Host "Current .env (without secrets):" -ForegroundColor Gray
Get-Content .env | Where-Object { $_ -notmatch "PIN" -and $_ -notmatch "PASSWORD" }

New-Item -ItemType Directory -Force -Path screenshots | Out-Null
New-Item -ItemType Directory -Force -Path reports | Out-Null

Write-Host ""
Write-Host "Running login tests (no credentials needed)..." -ForegroundColor Yellow
pytest tests/test_login.py -v

Write-Host ""
Write-Host "Running dashboard tests (needs credentials)..." -ForegroundColor Yellow
pytest tests/test_dashboard.py -v

Write-Host ""
Write-Host "Running full suite..." -ForegroundColor Yellow
pytest -v --html=reports/report.html --self-contained-html

Write-Host ""
Write-Host "Report: reports/report.html" -ForegroundColor Green
Write-Host "Screenshots: screenshots/" -ForegroundColor Green
