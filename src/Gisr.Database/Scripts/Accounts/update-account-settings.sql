UPDATE account
SET display_name = @DisplayName,
    contact_email = @ContactEmail,
    notify_on_service_record = @NotifyOnServiceRecord,
    theme = COALESCE(@Theme, theme)
WHERE id = @Id
RETURNING id, tenant_id, entra_object_id, display_name, sign_in_email, contact_email,
          operating_number, notify_on_service_record, theme, created_at, last_sign_in_at;
