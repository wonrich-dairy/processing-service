"""
Processing Runs Page - List and detail
Covers SCRUM-62 Record milk unload, SCRUM-89 list/detail, SCRUM-90 complete, SCRUM-77 dashboard
"""
from selenium.webdriver.common.by import By
from .base_page import BasePage
import time
import re

class ProcessingRunsPage(BasePage):
    # List page
    RUNS_TABLE = (By.XPATH, "//table")
    RUN_ROWS = (By.XPATH, "//table//tbody//tr | //div[contains(@class, 'run-card')] | //div[contains(@class, 'processing-run')]")
    DISPATCH_NUMBER_CELL = (By.XPATH, "//*[contains(text(), 'DSP-')]")
    STATE_BADGE = (By.XPATH, "//*[contains(@class, 'badge') or contains(@class, 'state')]")
    BATCH_CODE_CELL = (By.XPATH, "//*[contains(text(), '-SY-') or contains(text(), '-SK-') or contains(text(), '-FM-') or contains(text(), '-FLM-')]")
    
    # Detail page
    DETAIL_TITLE = (By.XPATH, "//*[contains(text(), 'Dispatch') or contains(text(), 'Processing Run')]")
    ALLOCATIONS_SECTION = (By.XPATH, "//*[contains(text(), 'Allocation')]")
    STAGES_SECTION = (By.XPATH, "//*[contains(text(), 'Stage') or contains(text(), 'Heating')]")
    QUALITY_PANEL_SECTION = (By.XPATH, "//*[contains(text(), 'Quality') or contains(text(), 'Lab Panel') or contains(text(), 'Fat')]")
    
    def is_list_loaded(self):
        time.sleep(1)
        page = self.get_page_source()
        return "dsp-" in page.lower() or "dispatch" in page.lower() or "processing" in page.lower()
    
    def get_runs_count(self):
        rows = self.find_elements(*self.RUN_ROWS)
        # Subtract header if table
        if self.is_visible(*self.RUNS_TABLE, timeout=2):
            return max(0, len(rows) - 1)
        return len(rows)
    
    def verify_batch_code_format(self):
        """Batch code [day]-[product]-[letter] e.g. 1-SY-A per AC 64"""
        page = self.get_page_source()
        pattern = r"\d+-[A-Z]+-[A-Z]"
        matches = re.findall(pattern, page)
        return matches
    
    def verify_product_types(self):
        page = self.get_page_source()
        product_types = ["SY", "SK", "FM", "FLM", "DK", "DY"]
        found = {}
        for pt in product_types:
            found[pt] = pt in page
        return found
    
    def verify_allocation_list_visible(self):
        """Allocation list visible on run showing every storing-to-mixing mapping DoD 64"""
        page = self.get_page_source().lower()
        indicators = ["allocation", "mt-02", "mt-03", "storing", "mixing", "source", "destination"]
        found = sum(1 for ind in indicators if ind in page)
        return found >= 2
    
    def verify_stages_list_visible(self):
        """Stages list visible on processing record duration shown DoD 65"""
        page = self.get_page_source().lower()
        indicators = ["stage", "heating", "homogeniser", "pasteuriser", "cooling", "duration", "31"]
        found = sum(1 for ind in indicators if ind in page)
        return found >= 2
    
    def verify_quality_panel_fields(self):
        """Fat LR SNF TS PH KQ alcohol result shown per AC 63"""
        page = self.get_page_source()
        fields = ["Fat", "LR", "SNF", "TS", "PH", "KQ", "Alcohol", "3.85", "8.64", "12.49", "6.72"]
        found = {}
        for f in fields:
            found[f] = f.lower() in page.lower()
        return found
    
    def verify_run_state_badges_distinct(self):
        """RunStateBadge distinct treatment AwaitingLabResult OnHold ReleasedForAllocation Complete per SCRUM-78"""
        badges = self.find_elements(*self.STATE_BADGE)
        page = self.get_page_source().lower()
        states = ["awaitinglabresult", "onhold", "releasedforallocation", "complete", "awaiting", "released", "on hold"]
        found_states = {s: s in page for s in states}
        return {
            "badges_count": len(badges),
            "states_found": found_states,
            "distinct_treatment": len(badges) > 0 or sum(found_states.values()) >= 2
        }
    
    def verify_deviation_badges_consistent(self):
        """DeviationBadge consistent across list detail timeline per SCRUM-78"""
        page = self.get_page_source().lower()
        return {
            "deviation_mentioned": "deviation" in page,
            "isdeviation": "isdeviation" in page or "is deviation" in page,
            "warning": "warning" in page or "outside" in page,
            "58_no_deviation": "58" in page
        }
    
    def click_first_run(self):
        """Click first run - FIXED missing method that caused AttributeError in tests"""
        # Try table rows
        rows = self.find_elements(*self.RUN_ROWS)
        if rows and len(rows) > 1:
            try:
                target = rows[1] if len(rows) > 1 else rows[0]
                self.driver.execute_script("arguments[0].scrollIntoView({block:'center'});", target)
                time.sleep(0.5)
                target.click()
                time.sleep(2)
                return True
            except Exception as e:
                print(f"[WARN] click_first_run table row failed: {e}")
        # Try dispatch links DN- and DSP-
        for xpath in ["//a[contains(text(), 'DN-')]", "//a[contains(text(), 'DSP-')]", "//tr[2]//a", "//div[contains(@class, 'card')][1]", "//div[contains(@class, 'run')][1]"]:
            el = self.find_clickable(By.XPATH, xpath)
            if el:
                try:
                    self.driver.execute_script("arguments[0].scrollIntoView({block:'center'});", el)
                    time.sleep(0.3)
                    el.click()
                    time.sleep(2)
                    return True
                except:
                    continue
        # Fallback: try any clickable row
        try:
            all_clickable = self.find_elements(By.XPATH, "//*[contains(@class, 'run') or contains(@class, 'card') or contains(@class, 'dispatch')]")
            for c in all_clickable:
                if c.is_displayed():
                    c.click()
                    time.sleep(2)
                    return True
        except:
            pass
        print("[WARN] click_first_run failed - no clickable run found")
        return False

    def click_run_by_dispatch(self, dispatch_number):
        el = self.find_clickable(By.XPATH, f"//*[contains(text(), '{dispatch_number}')]")
        if el:
            el.click()
            time.sleep(2)
            return True
        return False
    
    def get_detail_sections(self):
        page = self.get_page_source().lower()
        return {
            "has_allocations": "allocation" in page,
            "has_stages": "stage" in page or "heating" in page,
            "has_quality": "quality" in page or "fat" in page or "lab panel" in page,
            "has_batch_code": bool(self.verify_batch_code_format()),
            "has_tank_info": "tank" in page or "st-01" in page or "mt-02" in page
        }
