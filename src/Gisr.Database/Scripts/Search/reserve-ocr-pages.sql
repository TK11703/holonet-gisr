UPDATE ocr_usage SET pages_used = pages_used + @Pages
WHERE month = @Month AND pages_used + @Pages <= @Budget;