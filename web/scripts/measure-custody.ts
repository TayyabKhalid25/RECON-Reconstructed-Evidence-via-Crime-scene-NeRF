import { PrismaClient } from '@prisma/client'
import { appendAudit, verifyCustodyChain } from '../src/lib/custody'

const prisma = new PrismaClient()

async function main() {
  console.log('Starting custody overhead measurement...')
  
  // 1. Measure append overhead
  const ITERATIONS = 100
  let totalAppendTime = 0
  
  console.log(`Appending ${ITERATIONS} records sequentially...`)
  for (let i = 0; i < ITERATIONS; i++) {
    const start = performance.now()
    await appendAudit({
      action: 'measure-overhead',
      targetType: 'BENCHMARK',
      targetId: `run-${Date.now()}`,
    }, prisma)
    const end = performance.now()
    totalAppendTime += (end - start)
  }
  
  const avgAppendTimeMs = totalAppendTime / ITERATIONS
  console.log(`[Metrics] Average append time per row: ${avgAppendTimeMs.toFixed(2)} ms`)
  
  // 2. Measure verify time
  console.log('Verifying entire chain...')
  const verifyStart = performance.now()
  const result = await verifyCustodyChain(prisma)
  const verifyEnd = performance.now()
  
  if (!result.ok) {
    console.error('Verify failed!', result)
  } else {
    console.log(`[Metrics] Chain verification time for ${result.rows} rows: ${(verifyEnd - verifyStart).toFixed(2)} ms`)
  }
}

main()
  .catch(console.error)
  .finally(() => prisma.$disconnect())
