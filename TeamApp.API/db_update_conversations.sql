ALTER TABLE "Users"
ADD COLUMN IF NOT EXISTS "AvatarUrl" text NULL;

ALTER TABLE "Users"
ADD COLUMN IF NOT EXISTS "RefreshToken" text NULL;

ALTER TABLE "Users"
ADD COLUMN IF NOT EXISTS "RefreshTokenExpiry" timestamp with time zone NULL;

ALTER TABLE "Channels"
ADD COLUMN IF NOT EXISTS "ConversationType" text NOT NULL DEFAULT 'Group';

ALTER TABLE "Channels"
ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;

CREATE TABLE IF NOT EXISTS "ConversationMembers" (
    "ChannelId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "JoinedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_ConversationMembers" PRIMARY KEY ("ChannelId", "UserId"),
    CONSTRAINT "FK_ConversationMembers_Channels_ChannelId" FOREIGN KEY ("ChannelId") REFERENCES "Channels" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ConversationMembers_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_Channels_CreatedByUserId"
ON "Channels" ("CreatedByUserId");

CREATE INDEX IF NOT EXISTS "IX_ConversationMembers_UserId"
ON "ConversationMembers" ("UserId");

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_Channels_Users_CreatedByUserId'
    ) THEN
        ALTER TABLE "Channels"
        ADD CONSTRAINT "FK_Channels_Users_CreatedByUserId"
        FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id")
        ON DELETE SET NULL;
    END IF;
END $$;

UPDATE "Channels"
SET "ConversationType" = CASE
    WHEN "Name" LIKE 'DM\_%\_%' ESCAPE '\' THEN 'Direct'
    ELSE 'Group'
END;

INSERT INTO "ConversationMembers" ("ChannelId", "UserId", "JoinedAt")
SELECT c."Id", u."Id", NOW()
FROM "Channels" c
JOIN "Users" u
  ON u."AdUpn" = split_part(c."Name", '_', 2)
  OR u."AdUpn" = split_part(c."Name", '_', 3)
WHERE c."ConversationType" = 'Direct'
ON CONFLICT ("ChannelId", "UserId") DO NOTHING;

INSERT INTO "ConversationMembers" ("ChannelId", "UserId", "JoinedAt")
SELECT c."Id", u."Id", NOW()
FROM "Channels" c
JOIN "Messages" m ON m."ChannelId" = c."Id"
JOIN "Users" u ON u."Id" = m."SenderId"
WHERE c."ConversationType" = 'Group'
ON CONFLICT ("ChannelId", "UserId") DO NOTHING;
