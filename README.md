# Negocia

Painel de demonstração para registrar dívidas, simular propostas e acompanhar acordos. Feito com C#, ASP.NET Core MVC, Razor, Entity Framework Core e SQLite.

## Rodar no computador

Com o [SDK do .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) instalado, abre o terminal e executa:

```powershell
git clone https://github.com/samulino5757/sistema-neg-div.git
cd sistema-neg-div
dotnet run --urls http://localhost:5072
```

Abre [http://localhost:5072](http://localhost:5072). O banco é criado em `App_Data/negocia.db` e os registros são preservados entre execuções.

## Testar o fluxo

1. No painel, abre a ficha da **Marina Costa (demo)**. A dívida é de R$ 1.000,00.
2. Registra um contato. Depois simula **3 parcelas** com **10%** de desconto. O total deve ficar em **R$ 900,00**, dividido em três parcelas de R$ 300,00.
3. Confirma o acordo e usa **Dar baixa** numa parcela. O saldo do acordo deve cair para **R$ 600,00**.
4. Volta ao painel e abre a ficha do **João Lima (demo)** para ver um acordo com parcela vencida.
5. Usa **Novo cliente** e **Nova dívida** para testar o cadastro do zero.

## Regras da demonstração

- O desconto opcional aceita de 0 a 99,99%. As parcelas são mensais e sem juros.
- Os valores são armazenados em centavos. O saldo com desconto é arredondado para centavos e a última parcela absorve a diferença da divisão. R$ 1.000,00 em três parcelas sem desconto gera R$ 333,33, R$ 333,33 e R$ 333,34.
- As datas seguem o fuso de São Paulo. Uma parcela em aberto passa a vencida no dia seguinte ao vencimento.
- Cada dívida pode ter um acordo. O acordo é quitado quando todas as parcelas recebem baixa.
- Esta versão foi feita para rodar localmente com dados fictícios. A baixa é manual e representa apenas um registro no sistema.
