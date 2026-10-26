INSERT INTO account (id, tenant_id, entra_object_id, display_name, sign_in_email, created_at, last_sign_in_at)
VALUES (@Id, @TenantId, @EntraObjectId, @DisplayName, @SignInEmail, now(), now())
ON CONFLICT (tenant_id, entra_object_id)
DO UPDATE SET
    last_sign_in_at = now(),
    sign_in_email = COALESCE(EXCLUDED.sign_in_email, account.sign_in_email)
RETURNING id, tenant_id, entra_object_id, display_name, sign_in_email, contact_email,
          notify_on_service_record, created_at, last_sign_in_at;
