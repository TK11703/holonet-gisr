-- Any record can be sealed by an Officer, including one they wrote about themselves.
ALTER TABLE record DROP CONSTRAINT ck_record_sealed_is_service;
