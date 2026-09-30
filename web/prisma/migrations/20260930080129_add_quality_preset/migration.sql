-- CreateEnum
CREATE TYPE "QualityPreset" AS ENUM ('FAST', 'HIGH', 'MAX');

-- AlterTable
ALTER TABLE "Job" ADD COLUMN     "quality" "QualityPreset" NOT NULL DEFAULT 'FAST';

-- CreateTable
CREATE TABLE "Anchor" (
    "id" TEXT NOT NULL,
    "anchorId" TEXT NOT NULL,
    "provider" TEXT NOT NULL DEFAULT 'arcore-cloud-anchors',
    "posX" DOUBLE PRECISION NOT NULL,
    "posY" DOUBLE PRECISION NOT NULL,
    "posZ" DOUBLE PRECISION NOT NULL,
    "rotX" DOUBLE PRECISION NOT NULL,
    "rotY" DOUBLE PRECISION NOT NULL,
    "rotZ" DOUBLE PRECISION NOT NULL,
    "rotW" DOUBLE PRECISION NOT NULL,
    "scale" DOUBLE PRECISION NOT NULL DEFAULT 1,
    "expiresAt" TIMESTAMP(3),
    "deviceLabel" TEXT,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "sceneId" TEXT NOT NULL,
    "createdById" TEXT,

    CONSTRAINT "Anchor_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "Anchor_sceneId_createdAt_idx" ON "Anchor"("sceneId", "createdAt");

-- AddForeignKey
ALTER TABLE "Anchor" ADD CONSTRAINT "Anchor_sceneId_fkey" FOREIGN KEY ("sceneId") REFERENCES "Scene"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Anchor" ADD CONSTRAINT "Anchor_createdById_fkey" FOREIGN KEY ("createdById") REFERENCES "User"("id") ON DELETE SET NULL ON UPDATE CASCADE;
