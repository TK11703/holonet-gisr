INSERT INTO artifact (id, record_id, file_name, content_type, size_bytes, sha256, blob_name, thumbnail_blob_name, is_image, description,
                      uploaded_by_account_id, uploaded_at, index_status, index_detail)
SELECT @Id, @RecordId, @FileName, @ContentType, @SizeBytes, @Sha256, @BlobName, @ThumbnailBlobName, @IsImage, @Description,
       @UploadedByAccountId, now(),
       CASE WHEN @IsImage THEN 'skipped' ELSE 'pending' END,
       CASE WHEN @IsImage THEN 'unsupported_type' END
WHERE (SELECT count(*) FROM artifact WHERE record_id = @RecordId AND removed_at IS NULL) < @MaxActive;
