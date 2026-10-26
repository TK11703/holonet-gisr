SELECT id, record_id, name, awarded_on, issuer, description, sort_order
FROM record_award
WHERE record_id = @RecordId
ORDER BY sort_order;
