#!/bin/bash
# Wonrich Dairy Selenium - Run tests against Vercel live
# Vercel URL: https://frontend-phi-sage-81.vercel.app/

echo "Wonrich Dairy Selenium Tests - Vercel Live"
echo "URL: https://frontend-phi-sage-81.vercel.app/"
echo ""

# Check python
python3 --version

# Install deps
echo "Installing dependencies..."
pip install -r requirements.txt

# Check env
if [ ! -f .env ]; then
  echo "WARNING: .env not found, copying from .env.example"
  cp .env.example .env
  echo "Please edit .env with real Employee ID and PIN"
fi

echo ""
echo "Current .env:"
cat .env | grep -v PIN | grep -v PASSWORD

# Create dirs
mkdir -p screenshots reports

# Run tests
echo ""
echo "Running login tests (no credentials needed)..."
pytest tests/test_login.py -v

echo ""
echo "Running dashboard tests (needs credentials)..."
pytest tests/test_dashboard.py -v

echo ""
echo "Running full suite..."
pytest -v --html=reports/report.html --self-contained-html

echo ""
echo "Report: reports/report.html"
echo "Screenshots: screenshots/"
