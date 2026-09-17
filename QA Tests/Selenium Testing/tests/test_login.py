"""
Test Login - Secure Access
URL: https://frontend-phi-sage-81.vercel.app/
Covers: SCRUM-34 auth, SCRUM-71/74 health anonymous
"""
import pytest
import time
from pages.login_page import LoginPage
from config import BASE_URL, LOGIN_URL, EMPLOYEE_ID, PIN, EMPLOYEE_ID_ADMIN, PIN_ADMIN

class TestLogin:
    def test_login_page_loads(self, driver):
        """Login page loads with Secure Access, Wonrich Dairy, Field Management System"""
        login_page = LoginPage(driver)
        login_page.navigate(BASE_URL)
        time.sleep(2)
        assert login_page.is_login_page(), "Login page should show Secure Access, Wonrich Dairy, Employee ID, Log In"
        page_source = driver.page_source
        assert "Secure Access" in page_source or "Wonrich" in page_source or "Employee" in page_source
    
    def test_login_page_has_employee_id_and_pin_fields(self, driver):
        """Employee ID and PIN/Password fields visible"""
        login_page = LoginPage(driver)
        login_page.open()
        time.sleep(2)
        page_source = driver.page_source.lower()
        assert "employee" in page_source or "id" in page_source
        assert "pin" in page_source or "password" in page_source
        # Check inputs exist
        inputs = driver.find_elements("xpath", "//input")
        assert len(inputs) >= 2, "Should have at least 2 inputs (Employee ID + PIN)"
    
    def test_login_with_invalid_credentials_shows_error(self, driver):
        """Invalid credentials should show error - security per SCRUM-34"""
        login_page = LoginPage(driver)
        login_page.open()
        time.sleep(1)
        login_page.enter_employee_id("INVALID999")
        login_page.enter_pin("wrongpin")
        login_page.click_login()
        time.sleep(3)
        # Should still be on login page or show error
        page_source = driver.page_source.lower()
        # Either error message or still on login page
        is_still_login = login_page.is_login_page()
        has_error = "invalid" in page_source or "incorrect" in page_source or "failed" in page_source or "error" in page_source or is_still_login
        assert has_error or is_still_login, "Invalid login should show error or stay on login page"
    
    @pytest.mark.skipif("YOUR_" in EMPLOYEE_ID, reason="Credentials not set - fill .env with real Employee ID and PIN")
    def test_login_with_valid_qco_credentials(self, driver):
        """Valid QCO login should navigate to processing/dashboard"""
        login_page = LoginPage(driver)
        login_page.open()
        success = login_page.login(EMPLOYEE_ID, PIN)
        time.sleep(3)
        current_url = driver.current_url
        page_source = driver.page_source.lower()
        # Should be logged in
        assert login_page.is_logged_in() or "processing" in current_url.lower() or "dashboard" in current_url.lower() or "dispatch" in page_source or "tank" in page_source, f"Valid login should succeed, current URL: {current_url}"
    
    def test_secure_access_environment_text(self, driver):
        """Secure Access Environment text visible per login page"""
        login_page = LoginPage(driver)
        login_page.open()
        page_source = driver.page_source
        assert "Secure Access" in page_source or "Environment" in page_source or "Wonrich" in page_source
