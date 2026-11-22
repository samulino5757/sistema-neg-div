import { expect, test } from '@playwright/test';

test.use({ channel: 'msedge', baseURL: 'http://localhost:5072', viewport: { width: 390, height: 844 } });

test('cadastra, negocia e quita uma dívida usando React', async ({ page, request }) => {
  const csrf = await request.post('/api/clientes', { data: { nome: 'Não salvar' } });
  expect(csrf.status()).toBe(400);

  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Painel de cobrança' })).toBeVisible();
  await page.getByRole('searchbox').fill('joao');
  await expect(page.getByRole('link', { name: 'Abrir ficha de João Lima (demo)' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Abrir ficha de Marina Costa (demo)' })).toHaveCount(0);
  await page.getByLabel('Situação').selectOption('Quitado');
  await expect(page.getByText('Nenhuma dívida encontrada.')).toBeVisible();

  await page.getByRole('link', { name: 'Novo cliente' }).click();
  await page.getByLabel('Nome do cliente').fill('Rafa Demo React');
  await page.getByRole('button', { name: 'Salvar cliente' }).click();
  await expect(page.getByRole('heading', { name: 'Nova dívida' })).toBeVisible();
  await expect(page.getByLabel('Cliente').locator('option:checked')).toHaveText('Rafa Demo React');
  await page.getByLabel('Descrição').fill('Mensalidade React');
  await page.getByLabel('Valor original').fill('1000,00');
  await page.getByLabel('Vencimento').fill('2026-01-01');
  await page.getByRole('button', { name: 'Salvar dívida' }).click();
  await expect(page.getByRole('heading', { name: 'Rafa Demo React' })).toBeVisible();

  await page.getByLabel('Canal').selectOption({ label: 'WhatsApp' });
  await page.getByLabel('Resultado').selectOption({ label: 'Pediu parcelamento' });
  await page.getByLabel('Observação').fill('Contato pelo painel React');
  await page.getByRole('button', { name: 'Registrar contato' }).click();
  await expect(page.getByText('Contato pelo painel React')).toBeVisible();

  await page.getByRole('button', { name: 'Calcular proposta' }).click();
  await expect(page.getByText(/333,34/)).toBeVisible();
  await page.getByLabel('Desconto (%)').fill('10');
  await expect(page.getByRole('heading', { name: 'Proposta simulada' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Calcular proposta' }).click();
  await expect(page.getByText(/900,00/)).toBeVisible();
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Simular acordo' })).toBeVisible();
  await page.getByLabel('Desconto (%)').fill('10');
  await page.getByRole('button', { name: 'Calcular proposta' }).click();
  await page.getByRole('button', { name: 'Confirmar acordo' }).click();
  await expect(page.getByRole('heading', { name: 'Parcelas do acordo' })).toBeVisible();

  for (const [i, saldo] of ['600,00', '300,00', '0,00'].entries()) {
    await page.getByRole('button', { name: 'Dar baixa na parcela ' + (i + 1) }).click();
    await expect(page.locator('.metric').filter({ hasText: 'Saldo do acordo' })).toContainText(saldo);
  }
  await page.reload();
  await expect(page.getByText('Quitado', { exact: true })).toBeVisible();
  await expect(page.getByText('Contato pelo painel React')).toBeVisible();
});
