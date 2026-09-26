# Dominica Learn

Plataforma de gestão de conhecimento para estudo de longo prazo — concursos públicos, normas técnicas,
livros, base de conhecimento pessoal.

**Produto independente.** Domínio, banco, deploy e ciclo de vida próprios. Nenhuma regra de negócio é
compartilhada com a plataforma Dominica; a solution (`Dominica.Learn.slnx`) não referencia nenhum projeto
dela. Convive no mesmo repositório por conveniência de manutenção, e sai dele com um `git subtree split`
no dia em que isso fizer sentido.

O vault são arquivos `.md` numa pasta do disco. **A mesma pasta abre no Obsidian Desktop** — essa é a
promessa central, e é ela que determina a arquitetura inteira.

Leia **[docs/ARQUITETURA.md](docs/ARQUITETURA.md)**: explica por que cada decisão foi tomada e qual foi o
preço dela.

```bash
dotnet test Dominica.Learn.slnx     # 106 testes, sem banco e sem rede
dotnet run --project src/Dominica.Learn.Web
```
