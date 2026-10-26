UPDATE artifact SET index_status = @Status, index_detail = @Detail WHERE id = @Id AND removed_at IS NULL;
