"""
Dashboard Page - Processing Dashboard
URL: /processing or /dashboard
Covers SCRUM-77 Processing dashboard and run list with allocations and stages
FIXED for Vercel SPA 404 on direct /processing GET
"""
from selenium.webdriver.common.by import By
from .base_page import BasePage
from config import BASE_URL
import time

class DashboardPage(BasePage):
    DASHBOARD_TITLE = (By.XPATH, "//*[contains(text(), 'Dashboard') or contains(text(), 'Processing') or contains(text(), 'MCC') or contains(text(), 'Factory')]")
    RUN_LIST = (By.XPATH, "//*[contains(@class, 'run') or contains(@class, 'list') or //table or //div[contains(@class, 'card')]]")
    STATE_COUNTS = (By.XPATH, "//*[contains(text(), 'AwaitingLabResult') or contains(text(), 'OnHold') or contains(text(), 'ReleasedForAllocation') or contains(text(), 'Complete')]")
    PROCESSING_RUNS_TABLE = (By.XPATH, "//table | //div[contains(@class, 'table')] | //div[contains(@class, 'list')]")
    FILTER_BY_STATE = (By.XPATH, "//select | //button[contains(text(), 'Filter')] | //*[contains(text(), 'State')]")
    SEARCH_INPUT = (By.XPATH, "//input[contains(@placeholder, 'Search') or contains(@placeholder, 'Dispatch') or @type='search']")
    
    def navigate_to_processing_via_spa(self):
        """FIX for Vercel SPA 404 - Don't do driver.get(PROCESSING_URL) directly, navigate via BASE_URL + JS"""
        current_url = self.driver.current_url
        if "/processing" in current_url and "404" not in self.driver.page_source:
            return True
        
        # Try client-side navigation
        try:
            self.driver.get(BASE_URL)
            time.sleep(4)
            # App auto-redirects ProcessingTechnician to /processing via window.history.replaceState
            if "/processing" in self.driver.current_url:
                time.sleep(2)
                return True
            # Try JS pushState
            self.driver.execute_script("window.history.pushState({}, '', '/processing'); window.dispatchEvent(new PopStateEvent('popstate'));")
            time.sleep(3)
            # Try clicking Factory button
            try:
                factory_btns = self.driver.find_elements(By.XPATH, "//button[contains(text(), 'Factory')] | //*[contains(text(), 'Factory Service')] | //a[contains(@href, '/processing')]")
                for btn in factory_btns:
                    if btn.is_displayed():
                        btn.click()
                        time.sleep(3)
                        break
            except:
                pass
            return "/processing" in self.driver.current_url or "factory" in self.driver.page_source.lower()
        except Exception as e:
            print(f"[WARN] SPA navigation failed: {e}")
            return False
    
    def is_dashboard_loaded(self):
        time.sleep(2)
        # First try SPA navigation fix
        if "404: not_found" in self.driver.page_source or "404" in self.driver.title:
            self.navigate_to_processing_via_spa()
            time.sleep(2)
        
        page = self.get_page_source()
        keywords = ["processing", "dispatch", "tank", "run", "dashboard", "awaiting", "released", "onhold", "factory", "unloads", "tanks", "factory active"]
        found = sum(1 for k in keywords if k.lower() in page.lower())
        # Also check for Factory Active pill, tabbar
        has_factory_ui = "factory" in page.lower() or "tabbar" in page.lower() or "unloads" in page.lower()
        return found >= 2 or has_factory_ui
    
    def get_state_counts(self):
        page = self.get_page_source()
        states = {}
        for state in ["AwaitingLabResult", "OnHold", "ReleasedForAllocation", "Completed", "Complete"]:
            if state.lower() in page.lower():
                states[state] = True
        return states
    
    def get_runs_list_text(self):
        return self.get_page_source()
    
    def get_visible_runs(self):
        rows = self.find_elements(By.XPATH, "//table//tr | //div[contains(@class, 'card')] | //div[contains(@class, 'run')] | //li")
        return rows
    
    def check_run_state_badges(self):
        page = self.get_page_source().lower()
        badges = {}
        for state in ["awaitinglabresult", "onhold", "releasedforallocation", "complete", "awaiting", "released"]:
            badges[state] = state in page
        return badges
    
    def check_deviation_badges(self):
        page = self.get_page_source().lower()
        return {
            "deviation_present": "deviation" in page or "isdeviation" in page,
            "warning_present": "warning" in page or "outside" in page
        }
    
    def filter_by_state(self, state):
        if self.is_visible(By.XPATH, f"//option[contains(text(), '{state}')]", timeout=3):
            self.click(By.XPATH, f"//option[contains(text(), '{state}')]")
            time.sleep(1)
            return True
        if self.is_visible(By.XPATH, f"//*[contains(text(), '{state}')]", timeout=3):
            self.click(By.XPATH, f"//*[contains(text(), '{state}')]")
            time.sleep(1)
            return True
        selects = self.find_elements(By.TAG_NAME, "select")
        for sel in selects:
            options = sel.find_elements(By.TAG_NAME, "option")
            for opt in options:
                if state.lower() in opt.text.lower():
                    opt.click()
                    time.sleep(1)
                    return True
        return False
    
    def search_dispatch(self, dispatch_number):
        if self.is_visible(*self.SEARCH_INPUT, timeout=3):
            self.type(*self.SEARCH_INPUT, dispatch_number)
            time.sleep(1)
            return True
        inputs = self.find_elements(By.XPATH, "//input")
        for inp in inputs:
            try:
                inp.clear()
                inp.send_keys(dispatch_number)
                time.sleep(1)
                return True
            except:
                continue
        return False
    
    def click_first_run(self):
        rows = self.get_visible_runs()
        if rows and len(rows) > 1:
            try:
                target = rows[1] if len(rows) > 1 else rows[0]
                self.driver.execute_script("arguments[0].scrollIntoView({block:'center'});", target)
                time.sleep(0.5)
                target.click()
                time.sleep(2)
                return True
            except:
                pass
        dispatch_link = self.find_clickable(By.XPATH, "//a[contains(text(), 'DSP-')] | //a[contains(text(), 'DN-')] | //tr[2]//a | //div[contains(@class, 'card')][1]")
        if dispatch_link:
            dispatch_link.click()
            time.sleep(2)
            return True
        return False
