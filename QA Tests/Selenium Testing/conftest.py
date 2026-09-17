"""
Pytest conftest - WebDriver setup for Wonrich Dairy
Supports chrome, firefox, edge, headless, mobile 360px NFR5 per SCRUM-78
Fixed for Vercel SPA 404 on direct /processing - uses client-side navigation
Fixed for ChromeDriverManager network flakiness - uses cache fallback
"""
import pytest
import os
import time
import pathlib
from selenium import webdriver
from selenium.webdriver.chrome.service import Service as ChromeService
from selenium.webdriver.firefox.service import Service as FirefoxService
from selenium.webdriver.chrome.options import Options as ChromeOptions
from selenium.webdriver.firefox.options import Options as FirefoxOptions
from webdriver_manager.chrome import ChromeDriverManager
from webdriver_manager.firefox import GeckoDriverManager
from config import BROWSER, HEADLESS, WINDOW_WIDTH, WINDOW_HEIGHT, IMPLICIT_WAIT, PAGE_LOAD_TIMEOUT, BASE_URL

def get_chromedriver_path_with_fallback():
    """Get chromedriver path with cache fallback to avoid network errors like ChunkedEncodingError"""
    # Try cache first - look in webdriver-manager cache
    cache_dirs = [
        os.path.join(os.path.expanduser("~"), ".wdm", "drivers", "chromedriver"),
        os.path.join(os.path.expanduser("~"), ".cache", "selenium"),
        os.path.join(os.path.expanduser("~"), "AppData", "Local", "webdriver-manager")
    ]
    
    # Try to find existing chromedriver in cache
    for cache_dir in cache_dirs:
        if os.path.exists(cache_dir):
            for root, dirs, files in os.walk(cache_dir):
                for f in files:
                    if "chromedriver" in f.lower() and f.endswith(".exe"):
                        full_path = os.path.join(root, f)
                        if os.path.exists(full_path):
                            print(f"[INFO] Found cached chromedriver: {full_path}")
                            return full_path
    
    # Try ChromeDriverManager with retry
    max_retries = 3
    for attempt in range(max_retries):
        try:
            print(f"[INFO] Attempting ChromeDriverManager install (attempt {attempt+1}/{max_retries})")
            driver_path = ChromeDriverManager().install()
            print(f"[INFO] ChromeDriverManager success: {driver_path}")
            return driver_path
        except Exception as e:
            print(f"[WARN] ChromeDriverManager attempt {attempt+1} failed: {e}")
            if attempt < max_retries - 1:
                time.sleep(2)
                continue
            else:
                print("[WARN] All ChromeDriverManager attempts failed, trying fallback")
    
    # Fallback: try chromedriver in PATH
    try:
        import shutil
        chromedriver_in_path = shutil.which("chromedriver")
        if chromedriver_in_path:
            print(f"[INFO] Found chromedriver in PATH: {chromedriver_in_path}")
            return chromedriver_in_path
    except:
        pass
    
    # Last fallback: try default locations
    default_paths = [
        r"C:\Program Files\Google\Chrome\Application\chromedriver.exe",
        r"C:\chromedriver\chromedriver.exe",
        "/usr/bin/chromedriver",
        "/usr/local/bin/chromedriver"
    ]
    for p in default_paths:
        if os.path.exists(p):
            print(f"[INFO] Found chromedriver at default location: {p}")
            return p
    
    # If all fails, still try ChromeDriverManager one more time (will throw if fails)
    print("[WARN] No cached chromedriver found, trying ChromeDriverManager one last time")
    return ChromeDriverManager().install()

@pytest.fixture(scope="session")
def base_url():
    return BASE_URL

@pytest.fixture
def driver(request):
    browser = os.getenv("BROWSER", BROWSER).lower()
    headless = os.getenv("HEADLESS", str(HEADLESS)).lower() == "true"
    
    driver = None
    
    if browser == "chrome":
        options = ChromeOptions()
        if headless:
            options.add_argument("--headless=new")
        options.add_argument(f"--window-size={WINDOW_WIDTH},{WINDOW_HEIGHT}")
        options.add_argument("--no-sandbox")
        options.add_argument("--disable-dev-shm-usage")
        options.add_argument("--disable-gpu")
        options.add_argument("--disable-extensions")
        options.add_argument("--ignore-certificate-errors")
        options.add_argument("--disable-blink-features=AutomationControlled")
        # Additional stability options for Windows
        options.add_argument("--disable-dev-tools")
        options.add_argument("--disable-background-networking")
        if hasattr(request, 'param') and request.param == "mobile":
            mobile_emulation = {"deviceMetrics": {"width": 360, "height": 800, "pixelRatio": 3.0}}
            options.add_experimental_option("mobileEmulation", mobile_emulation)
        
        try:
            driver_path = get_chromedriver_path_with_fallback()
            service = ChromeService(executable_path=driver_path)
        except Exception as e:
            print(f"[WARN] Failed to get chromedriver path with fallback: {e}, trying default ChromeService")
            try:
                service = ChromeService(ChromeDriverManager().install())
            except:
                service = ChromeService()
        
        driver = webdriver.Chrome(service=service, options=options)
    
    elif browser == "firefox":
        options = FirefoxOptions()
        if headless:
            options.add_argument("--headless")
        try:
            service = FirefoxService(GeckoDriverManager().install())
        except:
            service = FirefoxService()
        driver = webdriver.Firefox(service=service, options=options)
        driver.set_window_size(WINDOW_WIDTH, WINDOW_HEIGHT)
    
    else:
        options = ChromeOptions()
        if headless:
            options.add_argument("--headless=new")
        options.add_argument(f"--window-size={WINDOW_WIDTH},{WINDOW_HEIGHT}")
        options.add_argument("--disable-blink-features=AutomationControlled")
        try:
            driver_path = get_chromedriver_path_with_fallback()
            service = ChromeService(executable_path=driver_path)
        except:
            try:
                service = ChromeService(ChromeDriverManager().install())
            except:
                service = ChromeService()
        driver = webdriver.Chrome(service=service, options=options)
    
    driver.implicitly_wait(IMPLICIT_WAIT)
    driver.set_page_load_timeout(PAGE_LOAD_TIMEOUT)
    
    yield driver
    
    try:
        driver.quit()
    except:
        pass

@pytest.fixture
def mobile_driver(request):
    options = ChromeOptions()
    mobile_emulation = {
        "deviceMetrics": {"width": 360, "height": 800, "pixelRatio": 3.0},
        "userAgent": "Mozilla/5.0 (Linux; Android 10; SM-G975F) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"
    }
    options.add_experimental_option("mobileEmulation", mobile_emulation)
    if os.getenv("HEADLESS", "false").lower() == "true":
        options.add_argument("--headless=new")
    options.add_argument("--disable-blink-features=AutomationControlled")
    options.add_argument("--disable-dev-tools")
    
    try:
        driver_path = get_chromedriver_path_with_fallback()
        service = ChromeService(executable_path=driver_path)
    except:
        try:
            service = ChromeService(ChromeDriverManager().install())
        except:
            service = ChromeService()
    
    driver = webdriver.Chrome(service=service, options=options)
    driver.implicitly_wait(IMPLICIT_WAIT)
    driver.set_page_load_timeout(PAGE_LOAD_TIMEOUT)
    
    yield driver
    
    try:
        driver.quit()
    except:
        pass

@pytest.fixture
def logged_in_driver(driver):
    from pages.login_page import LoginPage
    from config import EMPLOYEE_ID, PIN, BASE_URL

    if "YOUR_" in EMPLOYEE_ID or not EMPLOYEE_ID:
        print("\n[WARN] Credentials not set")
        driver.get(BASE_URL)
        yield driver
        return
    
    login_page = LoginPage(driver)
    login_page.open()
    success = login_page.login(EMPLOYEE_ID, PIN)
    if not success:
        print(f"\n[WARN] Login failed for {EMPLOYEE_ID}")
    
    time.sleep(3)
    current_url = driver.current_url
    print(f"\n[INFO] After login URL: {current_url}")
    
    if "/processing" not in current_url.lower():
        try:
            driver.execute_script("window.history.pushState({}, '', '/processing'); window.dispatchEvent(new PopStateEvent('popstate'));")
            time.sleep(2)
            try:
                factory_btn = driver.find_element("xpath", "//button[contains(text(), 'Factory')] | //a[contains(@href, '/processing')] | //*[contains(text(), 'Factory Service')]")
                if factory_btn.is_displayed():
                    factory_btn.click()
                    time.sleep(3)
            except:
                pass
        except Exception as e:
            print(f"[WARN] JS navigation failed: {e}")
    
    page_source = driver.page_source.lower()
    if "404: not_found" in page_source or ("404" in driver.title.lower() and "not_found" in page_source):
        print("[WARN] Got Vercel 404 on /processing direct - navigating via base URL")
        driver.get(BASE_URL)
        time.sleep(5)
        print(f"[INFO] After base reload URL: {driver.current_url}")
    
    time.sleep(2)
    yield driver

@pytest.fixture(scope="session", autouse=True)
def create_screenshots_dir():
    os.makedirs("screenshots", exist_ok=True)
    os.makedirs("reports", exist_ok=True)
