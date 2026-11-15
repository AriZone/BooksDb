-- BooksDB — SQLite schema (revision 1)
-- Redesign of the legacy SQL Server / Access schema.
--
-- Key changes from the old design:
--   * Authors / Translators / Illustrators  ->  one People table + Roles + BookContributors
--   * Languages + LanguagesOrig              ->  single Languages table, referenced twice
--   * Junctions use composite PKs with NOT NULL FKs (no orphan or duplicate links)
--   * INTEGER PRIMARY KEY throughout (SQLite-native; switch to TEXT GUIDs only if the
--     Android helper must mint IDs offline for sync)
--   * Borrower history via a Loans table (an open loan = currently lent out)
--   * Pictures: one row per image with an IsPrimary flag, instead of a circular FK
--
-- Language / translation handling:
--   Books.LanguageId          = language of THIS copy             (required)
--   Books.OriginalLanguageId  = source language IF a translation  (NULL otherwise)
--   Books.OriginalTitle       = title in the original language    (NULL otherwise)
--   "Is a translation" is derived: OriginalLanguageId IS NOT NULL.
--   Translators are linked through BookContributors with the 'Translator' role.

PRAGMA foreign_keys = ON;   -- connection-scoped: the app must re-enable this on every connection
PRAGMA user_version = 1;    -- bump on each migration

-- ---------- Lookups ----------

CREATE TABLE Roles (
    RoleId INTEGER PRIMARY KEY,
    Role   TEXT NOT NULL UNIQUE
);

CREATE TABLE Languages (
    LanguageId INTEGER PRIMARY KEY,
    Language   TEXT NOT NULL UNIQUE
);

CREATE TABLE Categories (
    CategoryId INTEGER PRIMARY KEY,
    Category   TEXT NOT NULL UNIQUE
);

CREATE TABLE Cities (
    CityId INTEGER PRIMARY KEY,
    City   TEXT NOT NULL UNIQUE
);

CREATE TABLE Covers (
    CoverId INTEGER PRIMARY KEY,
    Cover   TEXT NOT NULL UNIQUE
);

CREATE TABLE Publishers (
    PublisherId INTEGER PRIMARY KEY,
    Publisher   TEXT NOT NULL UNIQUE
);

CREATE TABLE Copyrights (
    CopyrightId INTEGER PRIMARY KEY,
    Copyright   TEXT NOT NULL UNIQUE
);

-- ---------- People & Borrowers ----------

CREATE TABLE People (
    PersonId   INTEGER PRIMARY KEY,
    FirstName  TEXT,
    SecondName TEXT,
    LastName   TEXT NOT NULL,
    SortName   TEXT,            -- e.g. 'Tuwim, Julian' — for Polish-correct alphabetical browse
    Notes      TEXT
);

CREATE TABLE Borrowers (
    BorrowerId  INTEGER PRIMARY KEY,
    FirstName   TEXT NOT NULL,
    LastName    TEXT NOT NULL,
    Email       TEXT,
    PhoneNumber TEXT,
    Notes       TEXT
);

-- ---------- Books ----------

CREATE TABLE Books (
    BookId             INTEGER PRIMARY KEY,
    Title              TEXT NOT NULL,
    OriginalTitle      TEXT,                                              -- set only when a translation
    LanguageId         INTEGER NOT NULL REFERENCES Languages(LanguageId),
    OriginalLanguageId INTEGER          REFERENCES Languages(LanguageId), -- set only when a translation
    YearPublished      INTEGER,
    YearCopyright      INTEGER,
    Edition            TEXT,
    OriginalEdition    TEXT,
    ISBN13             TEXT,                                              -- mostly NULL for the pre-ISBN collection
    ISBN10             TEXT,
    PublisherId        INTEGER REFERENCES Publishers(PublisherId),
    CityId             INTEGER REFERENCES Cities(CityId),
    CoverId            INTEGER REFERENCES Covers(CoverId),
    Pages              INTEGER,
    Description        TEXT,
    Shelf              TEXT,
    DateOfBuy          TEXT,                                              -- ISO-8601 'YYYY-MM-DD'
    InitialPrice       INTEGER,                                           -- store in grosze to avoid float rounding
    IsRead             INTEGER NOT NULL DEFAULT 0 CHECK (IsRead IN (0, 1)),
    DateOfLastRead     TEXT,
    Notes              TEXT
);

-- ---------- Junctions (composite PK: no orphans, no duplicates) ----------

CREATE TABLE BookContributors (
    BookId   INTEGER NOT NULL REFERENCES Books(BookId) ON DELETE CASCADE,
    PersonId INTEGER NOT NULL REFERENCES People(PersonId),
    RoleId   INTEGER NOT NULL REFERENCES Roles(RoleId),
    PRIMARY KEY (BookId, PersonId, RoleId)
);

CREATE TABLE BookCategories (
    BookId     INTEGER NOT NULL REFERENCES Books(BookId) ON DELETE CASCADE,
    CategoryId INTEGER NOT NULL REFERENCES Categories(CategoryId),
    PRIMARY KEY (BookId, CategoryId)
);

CREATE TABLE BookCopyrights (
    BookId      INTEGER NOT NULL REFERENCES Books(BookId) ON DELETE CASCADE,
    CopyrightId INTEGER NOT NULL REFERENCES Copyrights(CopyrightId),
    PRIMARY KEY (BookId, CopyrightId)
);

-- ---------- Pictures ----------

CREATE TABLE BookPictures (
    PictureId INTEGER PRIMARY KEY,
    BookId    INTEGER NOT NULL REFERENCES Books(BookId) ON DELETE CASCADE,
    Path      TEXT NOT NULL,                                  -- relative path keeps the DB portable
    IsPrimary INTEGER NOT NULL DEFAULT 0 CHECK (IsPrimary IN (0, 1))
);
-- at most one primary image per book
CREATE UNIQUE INDEX ux_bookpictures_primary ON BookPictures(BookId) WHERE IsPrimary = 1;

-- ---------- Loans (borrower history) ----------

CREATE TABLE Loans (
    LoanId       INTEGER PRIMARY KEY,
    BookId       INTEGER NOT NULL REFERENCES Books(BookId) ON DELETE CASCADE,
    BorrowerId   INTEGER NOT NULL REFERENCES Borrowers(BorrowerId),
    DateLent     TEXT NOT NULL,                               -- ISO-8601
    DateReturned TEXT,                                        -- NULL = still out
    Notes        TEXT
);
-- a book can only be out on one open loan at a time
CREATE UNIQUE INDEX ux_loans_open ON Loans(BookId) WHERE DateReturned IS NULL;

-- ---------- Indexes (SQLite does not auto-index foreign keys) ----------

CREATE INDEX ix_books_language     ON Books(LanguageId);
CREATE INDEX ix_books_origlanguage ON Books(OriginalLanguageId);
CREATE INDEX ix_books_publisher    ON Books(PublisherId);
CREATE INDEX ix_books_title        ON Books(Title);
CREATE INDEX ix_people_sortname    ON People(SortName);
CREATE INDEX ix_contrib_person     ON BookContributors(PersonId);
CREATE INDEX ix_contrib_role       ON BookContributors(RoleId);
CREATE INDEX ix_loans_book         ON Loans(BookId);
CREATE INDEX ix_loans_borrower     ON Loans(BorrowerId);

-- ---------- Browse-by-role views (the old per-role tables, without the duplication) ----------

CREATE VIEW Authors AS
    SELECT DISTINCT p.* FROM People p
    JOIN BookContributors bc ON bc.PersonId = p.PersonId
    JOIN Roles r ON r.RoleId = bc.RoleId
    WHERE r.Role = 'Author';

CREATE VIEW Translators AS
    SELECT DISTINCT p.* FROM People p
    JOIN BookContributors bc ON bc.PersonId = p.PersonId
    JOIN Roles r ON r.RoleId = bc.RoleId
    WHERE r.Role = 'Translator';

CREATE VIEW Illustrators AS
    SELECT DISTINCT p.* FROM People p
    JOIN BookContributors bc ON bc.PersonId = p.PersonId
    JOIN Roles r ON r.RoleId = bc.RoleId
    WHERE r.Role = 'Illustrator';

-- ---------- Seed data (adjust freely) ----------

INSERT INTO Roles (Role) VALUES ('Author'), ('Translator'), ('Illustrator'), ('Editor');
INSERT INTO Languages (Language) VALUES ('Polish'), ('English'), ('German'), ('French'), ('Russian');
INSERT INTO Covers (Cover) VALUES ('Hardcover'), ('Paperback');
