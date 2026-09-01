/**
 * Dev seed: one user per role plus a case to upload into.
 *
 *   npx tsx scripts/seed.ts
 *
 * Passwords are dev-only. Idempotent, so it is safe to re-run.
 */
import { PrismaClient, Role } from '@prisma/client'
import bcrypt from 'bcryptjs'

const prisma = new PrismaClient()

const USERS = [
  { email: 'admin@recon.local', role: Role.ADMIN, password: 'admin-dev' },
  { email: 'investigator@recon.local', role: Role.INVESTIGATOR, password: 'investigator-dev' },
  { email: 'viewer@recon.local', role: Role.VIEWER, password: 'viewer-dev' },
]

async function main() {
  for (const u of USERS) {
    const user = await prisma.user.upsert({
      where: { email: u.email },
      update: { role: u.role },
      create: {
        email: u.email,
        role: u.role,
        passwordHash: await bcrypt.hash(u.password, 10),
      },
      select: { id: true, email: true, role: true },
    })
    console.log(`user ${user.email} (${user.role}) ${user.id}`)
  }

  const investigator = await prisma.user.findUniqueOrThrow({
    where: { email: 'investigator@recon.local' },
    select: { id: true },
  })

  const existing = await prisma.case.findFirst({
    where: { ownerId: investigator.id, title: 'Mock scene 1' },
    select: { id: true },
  })

  const kase =
    existing ??
    (await prisma.case.create({
      data: {
        title: 'Mock scene 1',
        description: 'Seeded dev case for Track B first light',
        ownerId: investigator.id,
      },
      select: { id: true },
    }))

  console.log(`case ${kase.id} owned by investigator`)
  console.log('\nseed complete')
}

main()
  .catch((err) => {
    console.error(err)
    process.exit(1)
  })
  .finally(() => prisma.$disconnect())
