INSERT INTO account (id, tenant_id, entra_object_id, display_name, sign_in_email, created_at, last_sign_in_at)
VALUES (@Id, @TenantId, @EntraObjectId, @DisplayName, @SignInEmail, now(), NULL)
ON CONFLICT (tenant_id, entra_object_id)
DO UPDATE SET id = account.id
RETURNING id, tenant_id, entra_object_id, display_name, sign_in_email, contact_email,
          operating_number, notify_on_service_record, created_at, last_sign_in_at;
