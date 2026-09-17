"""
Login Page - Secure Access
URL: https://frontend-phi-sage-81.vercel.app/
Fields: Employee ID, PIN / Password, Log In button
"""
from selenium.webdriver.common.by import By
from .base_page import BasePage
from config import LOGIN_URL, PROCESSING_URL
import time

class LoginPage(BasePage):
    # Locators - using multiple strategies for React
    EMPLOYEE_ID_INPUT = (By.XPATH, "//input[contains(@placeholder, 'Employee ID') or @name='employeeId' or @id='employeeId' or contains(@placeholder, 'Employee')]")
    EMPLOYEE_ID_INPUT_FALLBACK = (By.XPATH, "//label[contains(text(), 'Employee ID')]/following::input[1] | //input[@type='text'][1]")
    PIN_INPUT = (By.XPATH, "//input[contains(@placeholder, 'PIN') or contains(@placeholder, 'Password') or @name='pin' or @name='password' or @type='password']")
    PIN_INPUT_FALLBACK = (By.XPATH, "//input[@type='password']")
    LOGIN_BUTTON = (By.XPATH, "//button[contains(text(), 'Log In') or contains(text(), 'Login') or contains(text(), 'Sign In') or @type='submit']")
    LOGIN_BUTTON_FALLBACK = (By.XPATH, "//button")
    ERROR_MESSAGE = (By.XPATH, "//*[contains(@class, 'error') or contains(@class, 'alert') or contains(text(), 'Invalid') or contains(text(), 'incorrect') or contains(text(), 'failed')]")
    SECURE_ACCESS_TEXT = (By.XPATH, "//*[contains(text(), 'Secure Access') or contains(text(), 'Wonrich Dairy')]")
    FIELD_MANAGEMENT_TEXT = (By.XPATH, "//*[contains(text(), 'Field Management System')]")
    
    def open(self):
        self.navigate(LOGIN_URL)
        time.sleep(2)
        return self.is_login_page()
    
    def is_login_page(self):
        # Check for Secure Access, Wonrich Dairy, Employee ID, Log In
        page = self.get_page_source()
        indicators = ["Secure Access", "Wonrich Dairy", "Employee ID", "Log In", "Field Management"]
        found = sum(1 for ind in indicators if ind.lower() in page.lower())
        return found >= 2
    
    def enter_employee_id(self, employee_id):
        # Try multiple locators
        el = self.find_visible(*self.EMPLOYEE_ID_INPUT, timeout=10)
        if not el:
            el = self.find_visible(*self.EMPLOYEE_ID_INPUT_FALLBACK, timeout=5)
        if not el:
            # Try first text input
            inputs = self.find_elements(By.XPATH, "//input[@type='text' or @type='number']")
            if inputs:
                el = inputs[0]
        if el:
            el.clear()
            el.send_keys(employee_id)
            return True
        return False
    
    def enter_pin(self, pin):
        el = self.find_visible(*self.PIN_INPUT, timeout=10)
        if not el:
            el = self.find_visible(*self.PIN_INPUT_FALLBACK, timeout=5)
        if el:
            el.clear()
            el.send_keys(pin)
            return True
        return False
    
    def click_login(self):
        btn = self.find_clickable(*self.LOGIN_BUTTON, timeout=10)
        if not btn:
            btn = self.find_clickable(*self.LOGIN_BUTTON_FALLBACK, timeout=5)
            # Filter for login button if multiple
            if btn:
                buttons = self.find_elements(By.XPATH, "//button")
                for b in buttons:
                    if "log" in b.text.lower() or "sign" in b.text.lower():
                        btn = b
                        break
        if btn:
            self.driver.execute_script("arguments[0].scrollIntoView({block:'center'});", btn)
            time.sleep(0.5)
            btn.click()
            time.sleep(3)  # Wait for React navigation
            return True
        return False
    
    def login(self, employee_id, pin):
        if not self.is_login_page():
            self.open()
        self.enter_employee_id(employee_id)
        time.sleep(0.5)
        self.enter_pin(pin)
        time.sleep(0.5)
        self.click_login()
        time.sleep(3)
        # Check if login succeeded - URL changed or error not present
        current_url = self.driver.current_url
        page_source = self.get_page_source().lower()
        if "processing" in current_url.lower() or "dashboard" in current_url.lower() or "mcc" in page_source or "dispatch" in page_source:
            return True
        # Check for error
        if self.is_visible(*self.ERROR_MESSAGE, timeout=3):
            return False
        # If still on login page, maybe failed
        if self.is_login_page() and len(current_url) < len(LOGIN_URL) + 10:
            # Still on login, check error
            return False
        return True
    
    def get_error_message(self):
        if self.is_visible(*self.ERROR_MESSAGE, timeout=3):
            return self.get_text(*self.ERROR_MESSAGE)
        # Search for any error-like text
        errors = self.find_all_by_text("Invalid", "*") + self.find_all_by_text("incorrect", "*") + self.find_all_by_text("failed", "*")
        if errors:
            return errors[0].text
        return ""
    
    def is_logged_in(self):
        current_url = self.driver.current_url
        page_source = self.get_page_source().lower()
        # Logged in if not on login page and sees processing/dashboard/mcc content
        if self.is_login_page():
            return False
        if "processing" in current_url or "dashboard" in current_url:
            return True
        if "dispatch" in page_source or "tank" in page_source or "processing" in page_source or "mcc" in page_source:
            return True
        return False
