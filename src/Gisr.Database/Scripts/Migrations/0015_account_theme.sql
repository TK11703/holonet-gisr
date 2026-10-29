ALTER TABLE account
    ADD COLUMN theme varchar(32) NOT NULL DEFAULT 'teal' CHECK (char_length(theme) BETWEEN 1 AND 32);
