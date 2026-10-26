SELECT id, record_id, occurred_on, title, description, sort_order
FROM record_incident
WHERE record_id = @RecordId
ORDER BY sort_order;
