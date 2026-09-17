"""
Wonrich Dairy - Selenium Config
Vercel URL: https://frontend-phi-sage-81.vercel.app/
Tech: Frontend React, Backend .NET
Covers SCRUMs 56,57,61,62,63,64,65,70,71,72,74,77,88,89,90 + 78 shared components

Fill credentials in .env file or edit here directly.
"""
import os
from dotenv import load_dotenv

load_dotenv()

# Base URLs - Real Azure staging discovered from frontend bundle 2026-09-16
BASE_URL = os.getenv("BASE_URL", "https://frontend-phi-sage-81.vercel.app")
PROCESSING_URL = f"{BASE_URL}/processing"
LOGIN_URL = BASE_URL  # root shows Secure Access login
# Real backend URLs from bundle: be = auth, Ke intake/processing
AUTH_BASE_URL = os.getenv("AUTH_BASE_URL", "https://wonrich-auth-app-f4dndrgcgzgjb5h4.malaysiawest-01.azurewebsites.net")
INTAKE_BASE_URL = os.getenv("INTAKE_BASE_URL", "https://wonrich-mcc-app-abhgfsaxc6a3eqfa.malaysiawest-01.azurewebsites.net")
API_BASE_URL = os.getenv("API_BASE_URL", "https://app-wonrich-processing-staging.azurewebsites.net")  # processing staging
API_HEALTH_URL = f"{API_BASE_URL}/health"
# For local docker-compose fallback
API_BASE_URL_LOCAL = "http://localhost:5210"

# Credentials - Provided by user for Vercel live https://frontend-phi-sage-81.vercel.app/
# Employee ID - processing, PIN - Wonrich@2026 - validated 2026-09-16
EMPLOYEE_ID = os.getenv("EMPLOYEE_ID", "processing")
PIN = os.getenv("PIN", "Wonrich@2026")
EMPLOYEE_ID_ADMIN = os.getenv("EMPLOYEE_ID_ADMIN", "processing")
PIN_ADMIN = os.getenv("PIN_ADMIN", "Wonrich@2026")

# Additional role accounts if available
EMPLOYEE_ID_OPERATOR = os.getenv("EMPLOYEE_ID_OPERATOR", "")
PIN_OPERATOR = os.getenv("PIN_OPERATOR", "")

# Test Data - from seed per DOD 57
STORING_TANK_CODE = "ST-01"
STORING_TANK_CAPACITY = "5000"
MIXING_TANK_MT02 = "MT-02"
MIXING_TANK_MT03 = "MT-03"
MIXING_CAPACITY = "3000"

DISPATCH_VALID = "DSP-2609-0142"
DISPATCH_SECOND = "DSP-2609-0143"
DISPATCH_UNKNOWN = "DSP-9999-0001"

# Quality Panel expected values per AC 63
FAT_PERCENT = "3.85"
SNF = "8.64"
TS = "12.49"
PH = "6.72"
LR = "28.5"
KQ_COLOUR = "White"
ALCOHOL_PASSED_80 = "Passed 80%"
VERDICT_ACCEPT = "Accept"
VERDICT_REJECT = "Reject"

# Processing Stage thresholds per AC 65 - config not hardcoded
HEATING_MIN = 40
HEATING_MAX = 65
PASTEURISER_MIN = 80
PASTEURISER_MAX = 85
TEMPERATURE_VALID_MIN = 1.0
TEMPERATURE_VALID_MAX = 3.0
TEMPERATURE_DEVIATION_EXAMPLE = 5.2

# Batch code pattern [day]-[product]-[letter] e.g. 1-SY-A
BATCH_CODE_PATTERN = r"\d+-[A-Z]+-[A-Z]"
PRODUCT_TYPES = ["SY", "SK", "FM", "FLM", "DK", "DY"]  # Fresh, Flavoured, Yogurt etc per AC 64
PRODUCT_LINE_FRESH = ["SY", "SK"]
PRODUCT_LINE_FLAVOURED = ["FM", "FLM"]
PRODUCT_LINE_YOGURT = ["SY", "SK", "DY"]  # Below 80% acceptable defaults to Yogurt per AC 64

# State machine per DOD 61
STATES = ["AwaitingLabResult", "ReleasedForAllocation", "OnHold", "Rejected", "Completed", "Allocated", "Heating", "Pasteurising", "Cooling"]
RUN_STATE_BADGES = ["AwaitingLabResult", "OnHold", "ReleasedForAllocation", "Complete"]  # per SCRUM-78

# Timeouts
IMPLICIT_WAIT = 5
EXPLICIT_WAIT = 20
PAGE_LOAD_TIMEOUT = 30

# Browser options
BROWSER = os.getenv("BROWSER", "chrome")  # chrome, firefox, edge
HEADLESS = os.getenv("HEADLESS", "false").lower() == "true"
WINDOW_WIDTH = 1920
WINDOW_HEIGHT = 1080
# NFR5 360px mobile check for shared components per SCRUM-78
MOBILE_WIDTH = 360
MOBILE_HEIGHT = 800

# Vercel specific
VERCEL_URL = "https://frontend-phi-sage-81.vercel.app"
