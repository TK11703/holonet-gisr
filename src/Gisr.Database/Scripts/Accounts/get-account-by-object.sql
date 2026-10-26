SELECT id, tenant_id, entra_object_id, display_name, sign_in_email, contact_email,
       notify_on_service_record, created_at, last_sign_in_at
FROM account
WHERE tenant_id = @TenantId AND entra_object_id = @EntraObjectId;
