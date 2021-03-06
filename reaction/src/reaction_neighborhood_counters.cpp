/****************************************************************************
 * Copyright (C) from 2009 to Present EPAM Systems.
 * 
 * This file is part of Indigo toolkit.
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 ***************************************************************************/

#include "reaction/reaction_neighborhood_counters.h"
#include "reaction/reaction.h"
#include "reaction/query_reaction.h"
#include "molecule/molecule_neighbourhood_counters.h"

using namespace indigo;

void ReactionAtomNeighbourhoodCounters::calculate(Reaction &reac) {
   int i;

   _counters.resize(reac.count());

   for (i = reac.begin(); i < reac.end(); i = reac.next(i))
      _counters[i].calculate(reac.getMolecule(i));
}

void ReactionAtomNeighbourhoodCounters::calculate(QueryReaction &reac) {
   int i;

   _counters.resize(reac.count());

   for (i = reac.begin(); i < reac.end(); i = reac.next(i))
      _counters[i].calculate(reac.getQueryMolecule(i));
}

const MoleculeAtomNeighbourhoodCounters & ReactionAtomNeighbourhoodCounters::getCounters (int idx) const
{
   return _counters[idx];
}
