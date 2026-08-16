-- index ix_lyntai_curated_memory_kind
CREATE INDEX ix_lyntai_curated_memory_kind ON lyntai_curated_memory(kind, enabled);

-- index ix_lyntai_curated_memory_task
CREATE INDEX ix_lyntai_curated_memory_task ON lyntai_curated_memory(task, enabled);

-- index ix_lyntai_curated_meta_kv
CREATE INDEX ix_lyntai_curated_meta_kv ON lyntai_curated_meta(key, value);

-- index ix_lyntai_job_claim
CREATE INDEX ix_lyntai_job_claim ON lyntai_job(lane, status, priority DESC, available_at);

-- index ix_lyntai_job_partition
CREATE INDEX ix_lyntai_job_partition ON lyntai_job(lane, partition_key);

-- index ix_lyntai_memory_edge_to
CREATE INDEX ix_lyntai_memory_edge_to ON lyntai_memory_edge(to_id);

-- index ix_lyntai_memory_expiry
CREATE INDEX ix_lyntai_memory_expiry ON lyntai_memory_entry(task_key, scope, expires_at);

-- index ix_lyntai_memory_node_salience
CREATE INDEX ix_lyntai_memory_node_salience
ON lyntai_memory_node(engine, task_key, scope, salience DESC);

-- index ix_lyntai_memory_node_scope
CREATE INDEX ix_lyntai_memory_node_scope ON lyntai_memory_node(engine, task_key, scope);

-- index ix_lyntai_memory_review_engine_id
CREATE INDEX ix_lyntai_memory_review_engine_id ON lyntai_memory_review(engine, id);

-- index ix_lyntai_memory_subject_lookup
CREATE INDEX ix_lyntai_memory_subject_lookup
ON lyntai_memory_subject(engine, subject, task_key, scope, node_id DESC);

-- index ix_lyntai_message_thread_seq
CREATE UNIQUE INDEX ix_lyntai_message_thread_seq ON lyntai_message(thread_id, seq);

-- index ix_lyntai_prompt_active
CREATE INDEX ix_lyntai_prompt_active ON lyntai_prompt_version(name) WHERE is_active = 1;

-- index ix_lyntai_response_cache_created
CREATE INDEX ix_lyntai_response_cache_created ON lyntai_response_cache(created_at);

-- index ix_lyntai_response_cache_expiry
CREATE INDEX ix_lyntai_response_cache_expiry ON lyntai_response_cache(expires_at);

-- index ix_lyntai_trace_step_session
CREATE INDEX ix_lyntai_trace_step_session ON lyntai_trace_step(session_id, seq);

-- index ux_lyntai_memory_dedup
CREATE UNIQUE INDEX ux_lyntai_memory_dedup ON lyntai_memory_entry(task_key, scope, content);

-- index ux_lyntai_memory_node_dedup
CREATE UNIQUE INDEX ux_lyntai_memory_node_dedup
ON lyntai_memory_node(engine, task_key, scope, content_hash);

-- index ux_lyntai_prompt_name_version
CREATE UNIQUE INDEX ux_lyntai_prompt_name_version ON lyntai_prompt_version(name, version);

-- index ux_lyntai_version_info
CREATE UNIQUE INDEX "ux_lyntai_version_info" ON "lyntai_version_info" ("Version" ASC);

-- table lyntai_curated_fts
CREATE VIRTUAL TABLE lyntai_curated_fts USING fts5(content, content='lyntai_curated_memory', content_rowid='id', tokenize='trigram');

-- table lyntai_curated_fts_config
CREATE TABLE 'lyntai_curated_fts_config'(k PRIMARY KEY, v) WITHOUT ROWID;

-- table lyntai_curated_fts_data
CREATE TABLE 'lyntai_curated_fts_data'(id INTEGER PRIMARY KEY, block BLOB);

-- table lyntai_curated_fts_docsize
CREATE TABLE 'lyntai_curated_fts_docsize'(id INTEGER PRIMARY KEY, sz BLOB);

-- table lyntai_curated_fts_idx
CREATE TABLE 'lyntai_curated_fts_idx'(segid, term, pgno, PRIMARY KEY(segid, term)) WITHOUT ROWID;

-- table lyntai_curated_memory
CREATE TABLE lyntai_curated_memory (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    kind       TEXT NOT NULL,
    content    TEXT NOT NULL,
    enabled    INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
, task TEXT NULL, scope TEXT NULL, metadata TEXT NULL);

-- table lyntai_curated_meta
CREATE TABLE lyntai_curated_meta (
    memory_id INTEGER NOT NULL REFERENCES lyntai_curated_memory(id) ON DELETE CASCADE,
    key       TEXT NOT NULL,
    value     TEXT NOT NULL,
    PRIMARY KEY (memory_id, key)
);

-- table lyntai_job
CREATE TABLE lyntai_job (
    id TEXT PRIMARY KEY,
    lane TEXT NOT NULL,
    type TEXT NOT NULL,
    payload TEXT NOT NULL,
    status TEXT NOT NULL,
    checkpoint TEXT NULL,
    attempts INTEGER NOT NULL,
    max_attempts INTEGER NOT NULL,
    last_error TEXT NULL,
    available_at TEXT NOT NULL,
    claimed_at TEXT NULL,
    claimed_by TEXT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    priority INTEGER NOT NULL DEFAULT 0,
    cancel_requested INTEGER NOT NULL DEFAULT 0,
    progress INTEGER NOT NULL DEFAULT 0,
    total INTEGER NOT NULL DEFAULT 0,
    stage TEXT NULL,
    step_log TEXT NULL,
    partition_key TEXT NULL
);

-- table lyntai_job_slot
CREATE TABLE lyntai_job_slot (
    slot_index INTEGER PRIMARY KEY,
    worker_id TEXT NULL,
    acquired_at TEXT NULL
);

-- table lyntai_kv
CREATE TABLE "lyntai_kv" ("key" TEXT NOT NULL, "value" TEXT NOT NULL, "updated_at" TEXT NOT NULL, CONSTRAINT "PK_lyntai_kv" PRIMARY KEY ("key"));

-- table lyntai_memory_edge
CREATE TABLE lyntai_memory_edge (
    from_id INTEGER NOT NULL REFERENCES lyntai_memory_node(id) ON DELETE CASCADE,
    to_id INTEGER NOT NULL REFERENCES lyntai_memory_node(id) ON DELETE CASCADE,
    kind TEXT NOT NULL DEFAULT '',
    weight REAL NOT NULL,
    strengthened_position REAL NOT NULL, strengthened_ordinal INTEGER NOT NULL DEFAULT 0, strengthened_chars INTEGER NOT NULL DEFAULT 0, strengthened_at TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000+00:00',
    PRIMARY KEY (from_id, to_id, kind)
);

-- table lyntai_memory_entry
CREATE TABLE lyntai_memory_entry (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    task_key TEXT NOT NULL,
    scope TEXT NOT NULL,
    content TEXT NOT NULL,
    created_at TEXT NOT NULL
, expires_at TEXT NULL, last_accessed_at TEXT NULL);

-- table lyntai_memory_fts
CREATE VIRTUAL TABLE lyntai_memory_fts USING fts5(content, content='lyntai_memory_entry', content_rowid='id', tokenize='trigram');

-- table lyntai_memory_fts_config
CREATE TABLE 'lyntai_memory_fts_config'(k PRIMARY KEY, v) WITHOUT ROWID;

-- table lyntai_memory_fts_data
CREATE TABLE 'lyntai_memory_fts_data'(id INTEGER PRIMARY KEY, block BLOB);

-- table lyntai_memory_fts_docsize
CREATE TABLE 'lyntai_memory_fts_docsize'(id INTEGER PRIMARY KEY, sz BLOB);

-- table lyntai_memory_fts_idx
CREATE TABLE 'lyntai_memory_fts_idx'(segid, term, pgno, PRIMARY KEY(segid, term)) WITHOUT ROWID;

-- table lyntai_memory_node
CREATE TABLE lyntai_memory_node (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    engine TEXT NOT NULL,
    task_key TEXT NOT NULL,
    scope TEXT NOT NULL,
    headline TEXT NOT NULL,
    content TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    grade INTEGER NOT NULL,
    metadata TEXT NULL,
    created_at TEXT NOT NULL,
    last_recalled_position REAL NOT NULL,
    recall_count INTEGER NOT NULL DEFAULT 0,
    stability REAL NOT NULL
, signals TEXT NULL, salience REAL NOT NULL DEFAULT 1, encoding_ordinal INTEGER NOT NULL DEFAULT 0, encoding_chars INTEGER NOT NULL DEFAULT 0, encoding_at TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000+00:00', provenance_retrievability INTEGER NOT NULL DEFAULT 0, provenance_salience INTEGER NOT NULL DEFAULT 0, difficulty REAL NOT NULL DEFAULT 5);

-- table lyntai_memory_node_fts
CREATE VIRTUAL TABLE lyntai_memory_node_fts USING fts5(
    headline, content, content='lyntai_memory_node', content_rowid='id', tokenize='trigram');

-- table lyntai_memory_node_fts_config
CREATE TABLE 'lyntai_memory_node_fts_config'(k PRIMARY KEY, v) WITHOUT ROWID;

-- table lyntai_memory_node_fts_data
CREATE TABLE 'lyntai_memory_node_fts_data'(id INTEGER PRIMARY KEY, block BLOB);

-- table lyntai_memory_node_fts_docsize
CREATE TABLE 'lyntai_memory_node_fts_docsize'(id INTEGER PRIMARY KEY, sz BLOB);

-- table lyntai_memory_node_fts_idx
CREATE TABLE 'lyntai_memory_node_fts_idx'(segid, term, pgno, PRIMARY KEY(segid, term)) WITHOUT ROWID;

-- table lyntai_memory_position
CREATE TABLE lyntai_memory_position (
    engine TEXT PRIMARY KEY,
    position REAL NOT NULL
, ordinal INTEGER NOT NULL DEFAULT 0, chars INTEGER NOT NULL DEFAULT 0, encoded_at TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000+00:00');

-- table lyntai_memory_review
CREATE TABLE lyntai_memory_review (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    engine TEXT NOT NULL,
    node_id INTEGER NOT NULL,
    batch_id TEXT NOT NULL,
    created_at TEXT NOT NULL,
    pre_age REAL NOT NULL,
    pre_stability REAL NOT NULL,
    pre_difficulty REAL NOT NULL,
    pre_strength REAL NOT NULL,
    pre_strength_age REAL NOT NULL,
    grade REAL NULL,
    post_stability REAL NOT NULL,
    post_difficulty REAL NOT NULL,
    provenance_retrievability INTEGER NOT NULL DEFAULT 0,
    -- What an IMemoryVerificationPolicy judged about this entry for the recall that logged it:
    -- 1 = it answered the query, 0 = it did not, NULL = no verifier ran.
    --
    -- NULLABLE, and the three states are NOT collapsible. `grade` above is derived from the
    -- curve's own prediction, so a fit against it recovers whatever produced the log (design
    -- DECISIONS D51). This column is the external observation that breaks that circularity —
    -- and, because a row is now written for entries that were NOT reinforced, it is also what
    -- lets the log contain FAILURES rather than only successes, which was D51's second and
    -- harder blocker.
    verified INTEGER NULL
);

-- table lyntai_memory_subject
CREATE TABLE lyntai_memory_subject (
    engine TEXT NOT NULL,
    node_id INTEGER NOT NULL REFERENCES lyntai_memory_node(id) ON DELETE CASCADE,
    task_key TEXT NOT NULL,
    scope TEXT NOT NULL,
    subject TEXT NOT NULL,
    PRIMARY KEY (engine, node_id, subject)
);

-- table lyntai_message
CREATE TABLE lyntai_message (
    id TEXT PRIMARY KEY,
    thread_id TEXT NOT NULL REFERENCES lyntai_thread(id) ON DELETE CASCADE,
    seq INTEGER NOT NULL,
    kind TEXT NOT NULL,
    payload TEXT NOT NULL,
    metadata TEXT NULL,
    created_at TEXT NOT NULL
);

-- table lyntai_prompt_version
CREATE TABLE lyntai_prompt_version (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    version INTEGER NOT NULL,
    template TEXT NOT NULL,
    author TEXT NULL,
    created_at TEXT NOT NULL,
    is_active INTEGER NOT NULL
);

-- table lyntai_response_cache
CREATE TABLE lyntai_response_cache (
    cache_key  TEXT PRIMARY KEY,
    reply_json TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    created_at TEXT NOT NULL
);

-- table lyntai_run_trace
CREATE TABLE "lyntai_run_trace" ("session_id" TEXT NOT NULL, "mode" TEXT NOT NULL, "started_at" TEXT NOT NULL, "ended_at" TEXT, trace_id TEXT NULL, CONSTRAINT "PK_lyntai_run_trace" PRIMARY KEY ("session_id"));

-- table lyntai_score_result
CREATE TABLE lyntai_score_result (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id TEXT NOT NULL,
    scorer_id TEXT NOT NULL,
    scorer_name TEXT NOT NULL,
    score_group TEXT NOT NULL,
    is_llm INTEGER NOT NULL,
    score REAL NOT NULL,
    reason TEXT NULL,
    created_at TEXT NOT NULL,
    UNIQUE(session_id, scorer_id)
);

-- table lyntai_thread
CREATE TABLE "lyntai_thread" ("id" TEXT NOT NULL, "title" TEXT, "created_at" TEXT NOT NULL, "metadata" TEXT, CONSTRAINT "PK_lyntai_thread" PRIMARY KEY ("id"));

-- table lyntai_trace_step
CREATE TABLE lyntai_trace_step (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id TEXT NOT NULL REFERENCES lyntai_run_trace(session_id) ON DELETE CASCADE,
    seq INTEGER NOT NULL,
    offset_ms INTEGER NOT NULL DEFAULT 0,
    kind TEXT NOT NULL,
    label TEXT NOT NULL,
    input_tokens INTEGER NOT NULL,
    output_tokens INTEGER NOT NULL,
    cost_usd REAL NOT NULL,
    duration_ms INTEGER NOT NULL,
    detail TEXT NULL
);

-- table lyntai_usage
CREATE TABLE lyntai_usage (
    consumer      TEXT PRIMARY KEY,
    input_tokens  INTEGER NOT NULL DEFAULT 0,
    output_tokens INTEGER NOT NULL DEFAULT 0,
    cost_usd      REAL    NOT NULL DEFAULT 0,
    calls         INTEGER NOT NULL DEFAULT 0
);

-- table lyntai_vector
CREATE TABLE lyntai_vector (
    collection TEXT NOT NULL,
    vec_id     TEXT NOT NULL,
    vector     TEXT NOT NULL,
    payload    TEXT NOT NULL,
    PRIMARY KEY (collection, vec_id)
);

-- trigger lyntai_curated_memory_ad
CREATE TRIGGER lyntai_curated_memory_ad AFTER DELETE ON lyntai_curated_memory BEGIN
    INSERT INTO lyntai_curated_fts(lyntai_curated_fts, rowid, content) VALUES ('delete', old.id, old.content);
END;

-- trigger lyntai_curated_memory_ai
CREATE TRIGGER lyntai_curated_memory_ai AFTER INSERT ON lyntai_curated_memory BEGIN
    INSERT INTO lyntai_curated_fts(rowid, content) VALUES (new.id, new.content);
END;

-- trigger lyntai_curated_memory_au
CREATE TRIGGER lyntai_curated_memory_au AFTER UPDATE ON lyntai_curated_memory BEGIN
    INSERT INTO lyntai_curated_fts(lyntai_curated_fts, rowid, content) VALUES ('delete', old.id, old.content);
    INSERT INTO lyntai_curated_fts(rowid, content) VALUES (new.id, new.content);
END;

-- trigger lyntai_memory_entry_ad
CREATE TRIGGER lyntai_memory_entry_ad AFTER DELETE ON lyntai_memory_entry BEGIN
    INSERT INTO lyntai_memory_fts(lyntai_memory_fts, rowid, content) VALUES ('delete', old.id, old.content);
END;

-- trigger lyntai_memory_entry_ai
CREATE TRIGGER lyntai_memory_entry_ai AFTER INSERT ON lyntai_memory_entry BEGIN
    INSERT INTO lyntai_memory_fts(rowid, content) VALUES (new.id, new.content);
END;

-- trigger lyntai_memory_entry_au
CREATE TRIGGER lyntai_memory_entry_au AFTER UPDATE OF content ON lyntai_memory_entry BEGIN
    INSERT INTO lyntai_memory_fts(lyntai_memory_fts, rowid, content) VALUES ('delete', old.id, old.content);
    INSERT INTO lyntai_memory_fts(rowid, content) VALUES (new.id, new.content);
END;

-- trigger lyntai_memory_node_ad
CREATE TRIGGER lyntai_memory_node_ad AFTER DELETE ON lyntai_memory_node BEGIN
    INSERT INTO lyntai_memory_node_fts(lyntai_memory_node_fts, rowid, headline, content)
    VALUES ('delete', old.id, old.headline, old.content);
END;

-- trigger lyntai_memory_node_ai
CREATE TRIGGER lyntai_memory_node_ai AFTER INSERT ON lyntai_memory_node BEGIN
    INSERT INTO lyntai_memory_node_fts(rowid, headline, content)
    VALUES (new.id, new.headline, new.content);
END;

-- trigger lyntai_memory_node_au
CREATE TRIGGER lyntai_memory_node_au AFTER UPDATE OF headline, content ON lyntai_memory_node BEGIN
    INSERT INTO lyntai_memory_node_fts(lyntai_memory_node_fts, rowid, headline, content)
    VALUES ('delete', old.id, old.headline, old.content);
    INSERT INTO lyntai_memory_node_fts(rowid, headline, content)
    VALUES (new.id, new.headline, new.content);
END;

