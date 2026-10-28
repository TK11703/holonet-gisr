SELECT id, tenant_id, entra_object_id, display_name, sign_in_email, contact_email,
    operating_number, notify_on_service_record, created_at, last_sign_in_at
FROM account
WHERE tenant_id = @TenantId
  AND operating_number IS NOT NULL
  AND starts_with(lower(operating_number), lower(@Term))
ORDER BY operating_number, display_name
LIMIT @Limit;
