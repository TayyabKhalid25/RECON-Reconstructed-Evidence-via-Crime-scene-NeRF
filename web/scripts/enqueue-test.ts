import { PrismaClient } from '@prisma/client'

const prisma = new PrismaClient()

async function main() {
  const investigator = await prisma.user.findUnique({
    where: { email: 'investigator@recon.local' },
    select: { id: true },
  })

  if (!investigator) {
    console.log("Investigator not found. Run db:seed first.");
    return;
  }

  const kase = await prisma.case.findFirst({
    where: { ownerId: investigator.id, title: 'Mock scene 1' },
    select: { id: true },
  })

  if (!kase) {
    console.log("Case not found. Run db:seed first.");
    return;
  }

  const scene = await prisma.scene.create({
    data: {
      caseId: kase.id,
      name: 'Test Worker Scene',
    }
  })

  const asset = await prisma.asset.create({
    data: {
      sceneId: scene.id,
      kind: 'SOURCE_VIDEO',
      storageKey: 'scenes/cmtobucsz0004686kkabajabb/20260825_161856.mp4',
      mimeType: 'video/mp4',
      byteSize: 1024,
      sha256: 'fakehash',
    }
  })

  // Create the PENDING Job
  const job = await prisma.job.create({
    data: {
      sceneId: scene.id,
      status: 'PENDING',
      quality: 'FAST'
    }
  })

  console.log(`Created Job ${job.id} for Scene ${scene.id} with Asset ${asset.id}`)
}

main().catch(console.error).finally(() => prisma.$disconnect())
