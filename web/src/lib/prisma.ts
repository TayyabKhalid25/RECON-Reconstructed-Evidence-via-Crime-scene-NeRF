import { PrismaClient } from '@prisma/client'

// Next dev hot-reloads this module on every edit. A plain `new PrismaClient()`
// would leak a connection pool per reload until Postgres refuses new
// connections, so the instance is cached on globalThis in development.
const globalForPrisma = globalThis as unknown as {
  prisma: PrismaClient | undefined
}

export const prisma =
  globalForPrisma.prisma ??
  new PrismaClient({
    log: process.env.NODE_ENV === 'development' ? ['warn', 'error'] : ['error'],
  })

if (process.env.NODE_ENV !== 'production') {
  globalForPrisma.prisma = prisma
}
