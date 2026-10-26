DELETE FROM record_view_grant WHERE revoked_at IS NOT NULL OR expires_at <= now();
