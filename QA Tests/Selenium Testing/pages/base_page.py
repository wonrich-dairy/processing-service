"""
Base Page - Page Object Model base for Wonrich Dairy React frontend
Handles explicit waits, React SPA quirks, Vercel deployment
"""
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC
from selenium.webdriver.common.by import By
from selenium.common.exceptions import TimeoutException, NoSuchElementException
import time
from config import EXPLICIT_WAIT

class BasePage:
    def __init__(self, driver):
        self.driver = driver
        self.wait = WebDriverWait(driver, EXPLICIT_WAIT)
    
    def navigate(self, url):
        self.driver.get(url)
        # Wait for React to hydrate
        time.sleep(1)
    
    def find_element(self, by, value, timeout=EXPLICIT_WAIT):
        try:
            return WebDriverWait(self.driver, timeout).until(
                EC.presence_of_element_located((by, value))
            )
        except TimeoutException:
            return None
    
    def find_elements(self, by, value, timeout=EXPLICIT_WAIT):
        try:
            return WebDriverWait(self.driver, timeout).until(
                EC.presence_of_all_elements_located((by, value))
            )
        except TimeoutException:
            return []
    
    def find_clickable(self, by, value, timeout=EXPLICIT_WAIT):
        try:
            return WebDriverWait(self.driver, timeout).until(
                EC.element_to_be_clickable((by, value))
            )
        except TimeoutException:
            return None
    
    def find_visible(self, by, value, timeout=EXPLICIT_WAIT):
        try:
            return WebDriverWait(self.driver, timeout).until(
                EC.visibility_of_element_located((by, value))
            )
        except TimeoutException:
            return None
    
    def click(self, by, value):
        el = self.find_clickable(by, value)
        if el:
            # Scroll into view for React
            self.driver.execute_script("arguments[0].scrollIntoView({block:'center'});", el)
            time.sleep(0.3)
            el.click()
            return True
        return False
    
    def type(self, by, value, text, clear=True):
        el = self.find_visible(by, value)
        if el:
            if clear:
                el.clear()
            el.send_keys(text)
            return True
        return False
    
    def get_text(self, by, value):
        el = self.find_visible(by, value)
        return el.text if el else ""
    
    def get_texts(self, by, value):
        els = self.find_elements(by, value)
        return [e.text for e in els]
    
    def is_visible(self, by, value, timeout=5):
        try:
            WebDriverWait(self.driver, timeout).until(
                EC.visibility_of_element_located((by, value))
            )
            return True
        except:
            return False
    
    def is_present(self, by, value, timeout=5):
        try:
            WebDriverWait(self.driver, timeout).until(
                EC.presence_of_element_located((by, value))
            )
            return True
        except:
            return False
    
    def wait_for_text(self, by, value, text, timeout=EXPLICIT_WAIT):
        try:
            WebDriverWait(self.driver, timeout).until(
                EC.text_to_be_present_in_element((by, value), text)
            )
            return True
        except:
            return False
    
    def wait_for_url_contains(self, text, timeout=EXPLICIT_WAIT):
        try:
            WebDriverWait(self.driver, timeout).until(
                EC.url_contains(text)
            )
            return True
        except:
            return False
    
    def scroll_down(self):
        self.driver.execute_script("window.scrollTo(0, document.body.scrollHeight);")
        time.sleep(0.5)
    
    def scroll_up(self):
        self.driver.execute_script("window.scrollTo(0, 0);")
        time.sleep(0.5)
    
    def get_page_source(self):
        return self.driver.page_source
    
    def take_screenshot(self, name):
        self.driver.save_screenshot(f"screenshots/{name}.png")
    
    def find_by_text(self, text, tag="*"):
        """Find element containing text - useful for React where ids change"""
        xpath = f"//{tag}[contains(text(), '{text}')]"
        return self.find_element(By.XPATH, xpath)
    
    def find_all_by_text(self, text, tag="*"):
        xpath = f"//{tag}[contains(text(), '{text}')]"
        return self.find_elements(By.XPATH, xpath)
    
    def find_input_by_placeholder(self, placeholder):
        return self.find_element(By.XPATH, f"//input[contains(@placeholder, '{placeholder}')]")
    
    def find_by_label(self, label_text):
        # Try label -> input association
        xpath = f"//label[contains(text(), '{label_text}')]/following::input[1] | //label[contains(text(), '{label_text}')]/..//input | //input[@placeholder='{label_text}']"
        return self.find_element(By.XPATH, xpath)
