-- 2.5: anonymous crash reports (exception type and cleaned stack frames only); kept 30 days.
CREATE TABLE social.crash_reports (
    id              uuid        PRIMARY KEY,
    app_version     text        NOT NULL CHECK (app_version ~ '^[0-9.]{1,20}$'),
    android_sdk     integer     NOT NULL CHECK (android_sdk BETWEEN 21 AND 100),
    exception_type  text        NOT NULL CHECK (char_length(exception_type) BETWEEN 1 AND 120),
    stack           text        NOT NULL CHECK (char_length(stack) <= 2000),
    created_at      timestamptz NOT NULL
);
CREATE INDEX crash_reports_created_idx ON social.crash_reports (created_at);
