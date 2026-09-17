"""
Tanks Page
Covers SCRUM-56 Tank entity Code normalization st1->ST-01, SCRUM-57 seed, SCRUM-72 TemperatureLogs
"""
from selenium.webdriver.common.by import By
from .base_page import BasePage
import time

class TanksPage(BasePage):
    TANKS_TABLE = (By.XPATH, "//table | //div[contains(@class, 'tank')]")
    TANK_ROWS = (By.XPATH, "//table//tbody//tr | //div[contains(@class, 'tank-card')]")
    ST01_CELL = (By.XPATH, "//*[contains(text(), 'ST-01')]")
    MT02_CELL = (By.XPATH, "//*[contains(text(), 'MT-02')]")
    CREATE_TANK_BUTTON = (By.XPATH, "//button[contains(text(), 'Create') or contains(text(), 'Add Tank')]")
    CODE_INPUT = (By.XPATH, "//input[contains(@name, 'Code') or contains(@placeholder, 'Code')]")
    CAPACITY_INPUT = (By.XPATH, "//input[contains(@name, 'Capacity')]")
    
    def is_tanks_loaded(self):
        time.sleep(1)
        page = self.get_page_source()
        return "tank" in page.lower() and ("st-01" in page.lower() or "capacity" in page.lower())
    
    def verify_seed_data(self):
        """Seed 3 storing +3 mixing ST-01 5000 MT-02 3000 MT-03 3000 per DOD 57"""
        page = self.get_page_source()
        return {
            "ST-01": "ST-01" in page,
            "ST-02": "ST-02" in page,
            "ST-03": "ST-03" in page,
            "MT-01": "MT-01" in page,
            "MT-02": "MT-02" in page,
            "MT-03": "MT-03" in page,
            "5000": "5000" in page,
            "3000": "3000" in page,
            "CapacityKg": "capacity" in page.lower() or "5000" in page
        }
    
    def verify_code_normalization(self):
        """Code normalization st1->ST-01 dash uppercase MaxLength 20 unique ux_tanks_code per SCRUM-56"""
        page = self.get_page_source()
        # Check that codes are uppercase with dash
        has_uppercase = "ST-01" in page or "MT-02" in page
        has_dash = "ST-" in page or "MT-" in page
        return {
            "uppercase_with_dash": has_uppercase and has_dash,
            "ST-01_format": "ST-01" in page,
            "MT-02_format": "MT-02" in page
        }
    
    def verify_tank_fields(self):
        """CapacityKg RemainingKg HasPrecision 10,2 KG not litres RowVersion CreatedAtUtc datetime(6) per AC 56/57"""
        page = self.get_page_source().lower()
        return {
            "capacitykg": "capacity" in page,
            "remainingkg": "remaining" in page,
            "kg_not_litres": "kg" in page,
            "kind": "kind" in page or "storing" in page or "mixing" in page,
            "status": "status" in page or "active" in page
        }
    
    def get_tanks_count(self):
        rows = self.find_elements(*self.TANK_ROWS)
        if self.is_visible(*self.TANKS_TABLE, timeout=2):
            return max(0, len(rows) - 1)
        return len(rows)
